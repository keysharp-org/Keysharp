using Keysharp.Builtins;
#if LINUX
using System.Runtime.InteropServices;
using Keysharp.Internals.Input.Linux;

using static Keysharp.Internals.Input.Keyboard.VirtualKeys;

namespace Keysharp.Internals.Input.Unix
{
	internal sealed class LinuxXkbCharMapperProvider : IKeyCodeMapperProvider
	{
		private readonly object initLock = new();
		private readonly Func<uint?> activeLayoutOverride;
		private readonly Func<DesktopKeyboardSnapshot> desktopStateOverride;
		private DesktopKeyboardSnapshot desktopSnapshot;
		private string appliedDesktopKeymap;
		private int operationDepth;

		private bool initTried;
		private bool ready;

		private string prefRules;
		private string prefModel;
		private string prefLayout;
		private string prefVariant;
		private string prefOptions;

		private IntPtr ctx;
		private IntPtr keymap;

		private int minKeycode;
		private int maxKeycode;
		private uint shiftIndex = uint.MaxValue;
		private uint mod5Index = uint.MaxValue;
		private bool hasModsForLevel;
		private uint lastDeadKeyLayout = uint.MaxValue;

		private readonly Dictionary<(uint keysym, uint layout), (uint vk, bool s, bool g)> cache = new(256);
		private readonly Dictionary<(uint vk, uint group), List<uint>> vkToKeycodesCache = new(128);
		private readonly Dictionary<uint, uint> keycodeToVkCache = new(128);

		// Pending dead-key composition for TranslateKeyWithDeadKeys: the combining mark used for
		// NFC composition with the following base character, plus the spacing form emitted when the
		// sequence does not compose (e.g. dead-grave then 'z' -> "`z").
		private readonly List<(char combining, char spacing)> pendingDead = new(3);

		// XKB dead keysyms -> (combining mark, spacing form). Covers the common Latin-script dead keys.
		// The combining mark is used for NFC composition; the spacing form is the fallback emitted when
		// the sequence does not compose.
		private static readonly Dictionary<uint, (char combining, char spacing)> deadKeysyms = new()
		{
			[0xfe50] = ('\u0300', '`'),       // dead_grave
			[0xfe51] = ('\u0301', '\u00B4'),  // dead_acute
			[0xfe52] = ('\u0302', '^'),       // dead_circumflex
			[0xfe53] = ('\u0303', '~'),       // dead_tilde
			[0xfe54] = ('\u0304', '\u00AF'),  // dead_macron
			[0xfe55] = ('\u0306', '\u02D8'),  // dead_breve
			[0xfe56] = ('\u0307', '\u02D9'),  // dead_abovedot
			[0xfe57] = ('\u0308', '\u00A8'),  // dead_diaeresis
			[0xfe58] = ('\u030A', '\u02DA'),  // dead_abovering
			[0xfe59] = ('\u030B', '\u02DD'),  // dead_doubleacute
			[0xfe5a] = ('\u030C', '\u02C7'),  // dead_caron
			[0xfe5b] = ('\u0327', '\u00B8'),  // dead_cedilla
			[0xfe5c] = ('\u0328', '\u02DB'),  // dead_ogonek
			[0xfe60] = ('\u0323', '.'),       // dead_belowdot
			[0xfe61] = ('\u0309', '?'),       // dead_hook
			[0xfe62] = ('\u031B', '\''),      // dead_horn
		};

		private static readonly Dictionary<string, uint> xkbName2Vk = new(StringComparer.Ordinal)
		{
			["TLDE"] = VK_OEM_3,
			["AE01"] = (uint)'1', ["AE02"] = (uint)'2', ["AE03"] = (uint)'3', ["AE04"] = (uint)'4',
			["AE05"] = (uint)'5', ["AE06"] = (uint)'6', ["AE07"] = (uint)'7', ["AE08"] = (uint)'8',
			["AE09"] = (uint)'9', ["AE10"] = (uint)'0', ["AE11"] = VK_OEM_MINUS, ["AE12"] = VK_OEM_PLUS,

			["AD01"] = (uint)'Q', ["AD02"] = (uint)'W', ["AD03"] = (uint)'E', ["AD04"] = (uint)'R',
			["AD05"] = (uint)'T', ["AD06"] = (uint)'Y', ["AD07"] = (uint)'U', ["AD08"] = (uint)'I',
			["AD09"] = (uint)'O', ["AD10"] = (uint)'P', ["AD11"] = VK_OEM_4, ["AD12"] = VK_OEM_6,

			["AC01"] = (uint)'A', ["AC02"] = (uint)'S', ["AC03"] = (uint)'D', ["AC04"] = (uint)'F',
			["AC05"] = (uint)'G', ["AC06"] = (uint)'H', ["AC07"] = (uint)'J', ["AC08"] = (uint)'K',
			["AC09"] = (uint)'L', ["AC10"] = VK_OEM_1, ["AC11"] = VK_OEM_7, ["BKSL"] = VK_OEM_5,

			["AB01"] = (uint)'Z', ["AB02"] = (uint)'X', ["AB03"] = (uint)'C', ["AB04"] = (uint)'V',
			["AB05"] = (uint)'B', ["AB06"] = (uint)'N', ["AB07"] = (uint)'M', ["AB08"] = VK_OEM_COMMA,
			["AB09"] = VK_OEM_PERIOD, ["AB10"] = VK_OEM_2, ["AB11"] = VK_OEM_102,

			["SPCE"] = VK_SPACE,
			["LSGT"] = VK_OEM_102,
			["AE13"] = VK_OEM_5,
			["HENK"] = VK_CONVERT,
			["MUHE"] = VK_NONCONVERT,
			["HKTG"] = VK_KANA,
			["KATA"] = VK_KANA,
			["HIRA"] = VK_KANA,
			["HNGL"] = VK_KANA,
			["HJCV"] = VK_HANJA,
			["JPCM"] = VK_SEPARATOR,
			["COMP"] = VK_APPS,
		};

		internal LinuxXkbCharMapperProvider(Func<uint?> activeLayoutOverride = null, Func<DesktopKeyboardSnapshot> desktopStateOverride = null)
		{
			this.activeLayoutOverride = activeLayoutOverride;
			this.desktopStateOverride = desktopStateOverride;
		}

		public bool TryMapRuneToKeystroke(Rune rune, nint? layout, out uint vk, out bool needShift, out bool needAltGr)
		{
			using var operation = EnterOperation();
			vk = 0;
			needShift = false;
			needAltGr = false;

			if (!TryGetReadyKeymap(out var currentKeymap))
				return false;

			uint keysym = KeysymFromRune(rune);

			if (keysym == 0)
				return false;

			// Use the group snapshotted once per send when supplied (KeybdLayoutRef.Value carries it as an
			// nint on Linux); otherwise resolve it live.
			var group = layout.HasValue ? (uint)layout.Value : GetActiveLayout();
			var cacheKey = (keysym, group);

			if (cache.TryGetValue(cacheKey, out var cached))
			{
				(vk, needShift, needAltGr) = cached;
				return true;
			}

			for (uint key = (uint)minKeycode; key <= (uint)maxKeycode; key++)
			{
				if (!TryGetNamedVk(key, out var candidateVk))
					continue;

				var keyLayout = NormalizeLayoutForKey(currentKeymap, key, group);
				int levels = xkb_keymap_num_levels_for_key(currentKeymap, key, keyLayout);

				if (levels <= 0)
					continue;

				for (uint level = 0; level < (uint)levels; level++)
				{
					if (!LevelHasKeysym(currentKeymap, key, keyLayout, level, keysym))
						continue;

					if (!TryResolveSupportedModsForLevel(key, keyLayout, level, out var s, out var g))
						continue;

					cache[cacheKey] = (candidateVk, s, g);
					vk = candidateVk;
					needShift = s;
					needAltGr = g;
					return true;
				}
			}

			return false;
		}

		public bool TryMapKeystrokeToRune(uint vk, bool shift, bool altGr, out Rune rune)
		{
			using var operation = EnterOperation();
			rune = default;

			if (!TryMapVkToXKeycode(vk, out var keycode, false))
				return false;

			if (!TryGetReadyKeymap(out var currentKeymap))
				return false;

			var layout = NormalizeLayoutForKey(currentKeymap, keycode, GetActiveLayout());
			int levels = xkb_keymap_num_levels_for_key(currentKeymap, keycode, layout);

			for (uint level = 0; level < (uint)levels; level++)
			{
				if (!TryResolveSupportedModsForLevel(keycode, layout, level, out var s, out var g))
					continue;

				if (s != shift || g != altGr)
					continue;

				int count = xkb_keymap_key_get_syms_by_level(currentKeymap, keycode, layout, level, out var symsPtr);

				if (count <= 0 || symsPtr == IntPtr.Zero)
					continue;

				unsafe
				{
					var syms = (uint*)symsPtr;
					uint cp = xkb_keysym_to_utf32(syms[0]);

					if (cp == 0)
						continue;

					rune = new Rune(cp);
					return true;
				}
			}

			return false;
		}

		public int TranslateKeyWithDeadKeys(uint vk, bool shift, bool altGr, bool capsLock, Span<char> buffer)
		{
			using var operation = EnterOperation();
			var activeLayout = GetActiveLayout();

			if (activeLayout != lastDeadKeyLayout)
			{
				pendingDead.Clear();
				lastDeadKeyLayout = activeLayout;
			}

			// Backspace cancels any pending dead key (AHK's {dead key}{BS} behavior): emit the dead
			// key's spacing form(s) followed by '\b' so the caller collects then erases them.
			if (vk == VK_BACK && pendingDead.Count > 0)
			{
				var bn = 0;

				foreach (var d in pendingDead)
					if (bn < buffer.Length)
						buffer[bn++] = d.spacing;

				if (bn < buffer.Length)
					buffer[bn++] = '\b';

				pendingDead.Clear();
				return bn;
			}

			// Reuse the layout group already resolved for this event.
			if (!TryGetKeysymForKeystroke(vk, shift, altGr, activeLayout, out var keysym))
				return KeyCodes.TranslateNotHandled;

			if (deadKeysyms.TryGetValue(keysym, out var dead)) // This key is a dead key.
			{
				if (pendingDead.Count < 3) // Match AHK's chained-dead-key limit.
					pendingDead.Add(dead);

				return -1;
			}

			uint cp = xkb_keysym_to_utf32(keysym);

			if (cp == 0)
				return 0; // No text (e.g. a function/navigation key); leave any pending dead key intact.

			var baseRune = new Rune(cp);

			// Caps Lock only toggles letter case (matches Platform.Keys.ToUnicode).
			if (capsLock && Rune.IsLetter(baseRune))
			{
				var upper = Rune.ToUpperInvariant(baseRune);
				var lower = Rune.ToLowerInvariant(baseRune);
				baseRune = baseRune == upper ? lower : upper;
			}

			if (pendingDead.Count == 0)
				return WriteRune(baseRune, buffer);

			// Compose the base character with the pending combining mark(s) via NFC.
			var sb = new StringBuilder();
			_ = sb.Append(baseRune.ToString());

			foreach (var d in pendingDead)
				_ = sb.Append(d.combining);

			var composed = sb.ToString().Normalize(NormalizationForm.FormC);
			int n;

			if (IsFullyComposed(composed)) // e.g. dead-grave + 'e' -> "è".
			{
				n = CopyToBuffer(composed, buffer);
			}
			else // No composition (e.g. dead-grave + 'z'): emit spacing dead char(s) then the base char.
			{
				n = 0;

				foreach (var d in pendingDead)
					if (n < buffer.Length)
						buffer[n++] = d.spacing;

				n += WriteRune(baseRune, n < buffer.Length ? buffer[n..] : Span<char>.Empty);
			}

			pendingDead.Clear();
			return n;
		}

		public void ResetDeadKeyState()
		{
			using var operation = EnterOperation();
			lock (initLock)
				pendingDead.Clear();
		}

		// activeLayout is the raw layout group snapshotted once per keystroke by the caller, so this method
		// stays consistent throughout this translation; it is normalized per-key below.
		private bool TryGetKeysymForKeystroke(uint vk, bool shift, bool altGr, uint activeLayout, out uint keysym)
		{
			keysym = 0;

			if (!TryMapVkToXKeycode(vk, out var keycode, false))
				return false;

			if (!TryGetReadyKeymap(out var currentKeymap))
				return false;

			var layout = NormalizeLayoutForKey(currentKeymap, keycode, activeLayout);
			int levels = xkb_keymap_num_levels_for_key(currentKeymap, keycode, layout);

			for (uint level = 0; level < (uint)levels; level++)
			{
				if (!TryResolveSupportedModsForLevel(keycode, layout, level, out var s, out var g))
					continue;

				if (s != shift || g != altGr)
					continue;

				int count = xkb_keymap_key_get_syms_by_level(currentKeymap, keycode, layout, level, out var symsPtr);

				if (count <= 0 || symsPtr == IntPtr.Zero)
					continue;

				unsafe
				{
					keysym = ((uint*)symsPtr)[0];
				}

				return keysym != 0;
			}

			return false;
		}

		private static int WriteRune(Rune rune, Span<char> buffer)
		{
			if (buffer.IsEmpty)
				return 0;

			return rune.TryEncodeToUtf16(buffer, out var written) ? written : 0;
		}

		private static int CopyToBuffer(string s, Span<char> buffer)
		{
			var n = Math.Min(s.Length, buffer.Length);

			for (var i = 0; i < n; i++)
				buffer[i] = s[i];

			return n;
		}

		private static bool IsFullyComposed(string s)
		{
			foreach (var ch in s)
			{
				var cat = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch);

				if (cat is System.Globalization.UnicodeCategory.NonSpacingMark
						or System.Globalization.UnicodeCategory.SpacingCombiningMark
						or System.Globalization.UnicodeCategory.EnclosingMark)
					return false;
			}

			return true;
		}

		public bool TryMapXKeycodeToVk(uint keycode, out uint vk)
		{
			using var operation = EnterOperation();
			vk = 0;

			if (keycode == 0 || !TryGetReadyKeymap(out _))
				return false;

			if (keycodeToVkCache.TryGetValue(keycode, out vk))
				return vk != 0;

			if (TryGetNamedVk(keycode, out vk))
			{
				keycodeToVkCache[keycode] = vk;
				return true;
			}

			keycodeToVkCache[keycode] = 0;
			return false;
		}

		public bool TryMapVkToXKeycode(uint vk, out uint keycode, bool returnSecondary)
		{
			using var operation = EnterOperation();
			keycode = 0;

			if (vk == 0 || !TryGetReadyKeymap(out _))
				return false;

			var cacheKey = (vk, GetActiveLayout());
			if (!vkToKeycodesCache.TryGetValue(cacheKey, out var keycodes))
				vkToKeycodesCache[cacheKey] = keycodes = BuildKeycodesForVk(vk);

			if (keycodes.Count == 0)
				return false;

			keycode = keycodes[Math.Min(returnSecondary ? 1 : 0, keycodes.Count - 1)];
			return true;
		}

		public void ConfigureLayout(string rules, string model, string layout, string variant, string options)
		{
			lock (initLock)
			{
				prefRules = rules;
				prefModel = model;
				prefLayout = layout;
				prefVariant = variant;
				prefOptions = options;
				ResetState();
			}
		}

		public nint GetCurrentKeymapHandle()
		{
			using var operation = EnterOperation();
			return TryGetReadyKeymap(out var currentKeymap) ? currentKeymap : nint.Zero;
		}

		public uint GetActiveLayoutGroup()
		{
			using var operation = EnterOperation();
			return GetActiveLayout();
		}

		public string GetCurrentKeymapName()
		{
			using var operation = EnterOperation();
			if (TryGetReadyKeymap(out var currentKeymap))
			{
				var activeLayout = NormalizeLayoutIndex(currentKeymap, GetActiveLayout());
				var layoutName = GetLayoutName(currentKeymap, activeLayout);

				if (!string.IsNullOrEmpty(layoutName))
					return layoutName;
			}

			return GetConfiguredLayoutName();
		}

		public nint ResolveKeyboardLayout(string layout)
		{
			using var operation = EnterOperation();
			// Read-only, matching the macOS provider: this provider owns a single process-wide keymap, so
			// reconfiguring it here (ConfigureLayout rebuilds the keymap and nothing restores it) would
			// persistently change the layout used by every subsequent Send/hotstring. A key-info query must
			// not have that side effect, so the requested layout is ignored and the current handle returned.
			_ = layout;
			return GetCurrentKeymapHandle();
		}

		public void Dispose()
		{
			lock (initLock)
				ResetState();
		}

		private Operation EnterOperation()
		{
			System.Threading.Monitor.Enter(initLock);
			try
			{
				if (operationDepth == 0 && !HasPreferredLayoutNames())
				{
					var snapshot = desktopStateOverride != null ? desktopStateOverride() : DesktopKeyboardState.Current.Get();
					if (snapshot?.Keymap != appliedDesktopKeymap)
					{
						ResetState();
						appliedDesktopKeymap = snapshot?.Keymap;
					}
					desktopSnapshot = snapshot;
				}
				operationDepth++;
				return new Operation(this);
			}
			catch
			{
				System.Threading.Monitor.Exit(initLock);
				throw;
			}
		}

		private readonly struct Operation(LinuxXkbCharMapperProvider owner) : IDisposable
		{
			public void Dispose()
			{
				owner.operationDepth--;
				System.Threading.Monitor.Exit(owner.initLock);
			}
		}

		private void EnsureInitialized()
		{
			if (ready || initTried)
				return;

			lock (initLock)
			{
				if (ready || initTried)
					return;

				initTried = true;

				try
				{
					ctx = xkb_context_new(0);

					if (ctx == IntPtr.Zero)
						return;

					if (HasPreferredLayoutNames() && BuildKeymapFromNames())
					{
						FinalizeKeymapCommon();
						ready = true;
						return;
					}

					if (BuildKeymapFromDisplay())
					{
						FinalizeKeymapCommon();
						ready = true;
						return;
					}

					if (BuildKeymapFromNames())
					{
						FinalizeKeymapCommon();
						ready = true;
						return;
					}

					ResetNativeHandles();
				}
				catch
				{
					ready = false;
					ResetNativeHandles();
				}
			}
		}

		private bool HasPreferredLayoutNames()
			=> prefRules != null || prefModel != null || prefLayout != null || prefVariant != null || prefOptions != null;

		private bool TryGetReadyKeymap(out IntPtr currentKeymap)
		{
			EnsureInitialized();
			currentKeymap = keymap;
			return ready && currentKeymap != IntPtr.Zero;
		}

		private bool TryGetNamedVk(uint keycode, out uint vk)
		{
			vk = 0;

			if (!TryGetReadyKeymap(out var currentKeymap))
				return false;

			var name = xkb_keymap_key_get_name_safe(currentKeymap, keycode);
			return !string.IsNullOrEmpty(name) && xkbName2Vk.TryGetValue(name, out vk);
		}

		private List<uint> BuildKeycodesForVk(uint targetVk)
		{
			var keycodes = new List<uint>(2);

			if (!TryGetReadyKeymap(out _))
				return keycodes;

			for (uint key = (uint)minKeycode; key <= (uint)maxKeycode; key++)
			{
				if (TryGetNamedVk(key, out var candidateVk) && candidateVk == targetVk)
					keycodes.Add(key);
			}

			if (keycodes.Count == 0)
			{
				var symbols = KeyCodes.VkToKeysyms(targetVk);
				for (uint key = (uint)minKeycode; key <= (uint)maxKeycode; key++)
				{
					var group = NormalizeLayoutForKey(keymap, key, GetActiveLayout());
					var levels = xkb_keymap_num_levels_for_key(keymap, key, group);
					for (uint level = 0; level < levels; level++)
					{
						if (!symbols.Any(symbol => LevelHasKeysym(keymap, key, group, level, symbol))) continue;
						keycodes.Add(key);
						break;
					}
				}
			}

			return keycodes;
		}

		private bool BuildKeymapFromDisplay()
		{
			var text = desktopSnapshot?.Keymap;
			if (string.IsNullOrEmpty(text)) return false;
			keymap = xkb_keymap_new_from_string(ctx, text, XkbKeymapFormatTextV1, 0);
			return keymap != IntPtr.Zero;
		}

		private uint GetActiveLayout()
		{
			if (activeLayoutOverride != null)
			{
				var overridden = activeLayoutOverride();

				if (overridden.HasValue)
					return overridden.Value;
			}

			// A compositor may provide the map without a globally active group. Use its
			// first layout as the same fallback used for an unavailable layout query.
			return desktopSnapshot?.Group ?? 0;
		}

		private static uint NormalizeLayoutForKey(IntPtr map, uint key, uint layout)
		{
			if (layout == 0)
				return 0;

			int layouts = xkb_keymap_num_layouts_for_key(map, key);
			return layouts > 0 && layout < (uint)layouts ? layout : 0;
		}

		private static uint NormalizeLayoutIndex(IntPtr map, uint layout)
		{
			int layouts = xkb_keymap_num_layouts(map);
			return layouts > 0 && layout < (uint)layouts ? layout : 0;
		}

		private static string GetLayoutName(IntPtr map, uint layout)
		{
			var ptr = xkb_keymap_layout_get_name(map, layout);
			return ptr == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(ptr) ?? "";
		}

		private string GetConfiguredLayoutName()
		{
			string layout = prefLayout ?? Environment.GetEnvironmentVariable("XKB_DEFAULT_LAYOUT") ?? "";
			string variant = prefVariant ?? Environment.GetEnvironmentVariable("XKB_DEFAULT_VARIANT") ?? "";

			if (layout == "")
				return "";

			return variant == "" ? layout : $"{layout}:{variant}";
		}

		private bool BuildKeymapFromNames()
		{
			string rules = prefRules ?? Environment.GetEnvironmentVariable("XKB_DEFAULT_RULES");
			string model = prefModel ?? Environment.GetEnvironmentVariable("XKB_DEFAULT_MODEL");
			string layout = prefLayout ?? Environment.GetEnvironmentVariable("XKB_DEFAULT_LAYOUT");
			string variant = prefVariant ?? Environment.GetEnvironmentVariable("XKB_DEFAULT_VARIANT");
			string options = prefOptions ?? Environment.GetEnvironmentVariable("XKB_DEFAULT_OPTIONS");

			var names = new xkb_rule_names();
			var pins = new List<IntPtr>();

			try
			{
				if (!string.IsNullOrEmpty(rules)) names.rules = AllocUtf8(rules, pins);
				if (!string.IsNullOrEmpty(model)) names.model = AllocUtf8(model, pins);
				if (!string.IsNullOrEmpty(layout)) names.layout = AllocUtf8(layout, pins);
				if (!string.IsNullOrEmpty(variant)) names.variant = AllocUtf8(variant, pins);
				if (!string.IsNullOrEmpty(options)) names.options = AllocUtf8(options, pins);

				keymap = xkb_keymap_new_from_names(ctx, ref names, 0);
				return keymap != IntPtr.Zero;
			}
			finally
			{
				foreach (var ptr in pins)
					if (ptr != IntPtr.Zero)
						Marshal.FreeHGlobal(ptr);
			}
		}

		private void FinalizeKeymapCommon()
		{
			minKeycode = xkb_keymap_min_keycode(keymap);
			maxKeycode = xkb_keymap_max_keycode(keymap);
			shiftIndex = xkb_keymap_mod_get_index(keymap, "Shift");

			mod5Index = xkb_keymap_mod_get_index(keymap, "Mod5");

			if (mod5Index == uint.MaxValue)
			{
				mod5Index = xkb_keymap_mod_get_index(keymap, "AltGr");

				if (mod5Index == uint.MaxValue)
					mod5Index = xkb_keymap_mod_get_index(keymap, "ISO_Level3_Shift");
			}

			try
			{
				xkb_keymap_key_get_mods_for_level(keymap, 0, 0, 0, IntPtr.Zero, 0);
				hasModsForLevel = true;
			}
			catch (EntryPointNotFoundException)
			{
				hasModsForLevel = false;
			}
		}

		private bool TryResolveSupportedModsForLevel(uint key, uint layout, uint level, out bool needShift, out bool needAltGr)
		{
			needShift = false;
			needAltGr = false;

			if (hasModsForLevel)
			{
				Span<uint> buf = stackalloc uint[4];
				nuint got;

				unsafe
				{
					fixed (uint* p = buf)
						got = xkb_keymap_key_get_mods_for_level(keymap, key, layout, level, (IntPtr)p, (nuint)buf.Length);
				}

				if (got > 0)
				{
					uint supportedMask = 0;

					if (shiftIndex < 32)
						supportedMask |= 1u << (int)shiftIndex;

					if (mod5Index < 32)
						supportedMask |= 1u << (int)mod5Index;

					var count = got > (nuint)buf.Length ? buf.Length : (int)got;

					for (var i = 0; i < count; i++)
					{
						var mask = buf[i];

						if ((mask & ~supportedMask) != 0)
							continue;

						needShift = shiftIndex < 32 && (mask & (1u << (int)shiftIndex)) != 0;
						needAltGr = mod5Index < 32 && (mask & (1u << (int)mod5Index)) != 0;
						return true;
					}

					return false;
				}
			}

			switch (level)
			{
				case 0:
					return true;
				case 1:
					needShift = true;
					return true;
				case 2:
					needAltGr = true;
					return true;
				case 3:
					needShift = true;
					needAltGr = true;
					return true;
				default:
					return false;
			}
		}

		private static bool LevelHasKeysym(IntPtr map, uint key, uint layout, uint level, uint target)
		{
			IntPtr symsPtr;
			int count = xkb_keymap_key_get_syms_by_level(map, key, layout, level, out symsPtr);

			if (count <= 0 || symsPtr == IntPtr.Zero)
				return false;

			unsafe
			{
				var syms = (uint*)symsPtr;

				for (int i = 0; i < count; i++)
				{
					if (syms[i] == target)
						return true;
				}
			}

			return false;
		}

		private static string xkb_keymap_key_get_name_safe(IntPtr map, uint key)
		{
			var ptr = xkb_keymap_key_get_name(map, key);
			return ptr == IntPtr.Zero ? null : Marshal.PtrToStringAnsi(ptr);
		}

		private static uint KeysymFromRune(Rune rune)
		{
			uint cp = (uint)rune.Value;

			if (cp <= 0x7F)
				return cp;

			if (cp >= 0x00A0 && cp <= 0x00FF)
				return cp;

			if (cp >= 0x0100 && cp <= 0x10FFFF)
				return 0x01000000u | cp;

			return 0;
		}

		private static IntPtr AllocUtf8(string text, List<IntPtr> pins)
		{
			var bytes = Encoding.UTF8.GetBytes(text + "\0");
			var mem = Marshal.AllocHGlobal(bytes.Length);
			Marshal.Copy(bytes, 0, mem, bytes.Length);
			pins.Add(mem);
			return mem;
		}

		private void ResetState()
		{
			ResetNativeHandles();
			cache.Clear();
			vkToKeycodesCache.Clear();
			keycodeToVkCache.Clear();
			pendingDead.Clear();
			ready = false;
			initTried = false;
			minKeycode = 0;
			maxKeycode = 0;
			shiftIndex = uint.MaxValue;
			mod5Index = uint.MaxValue;
			hasModsForLevel = false;
			lastDeadKeyLayout = uint.MaxValue;

		}

		private void ResetNativeHandles()
		{
			if (keymap != IntPtr.Zero)
			{
				xkb_keymap_unref(keymap);
				keymap = IntPtr.Zero;
			}

			if (ctx != IntPtr.Zero)
			{
				xkb_context_unref(ctx);
				ctx = IntPtr.Zero;
			}
		}

		private const string LibXkbCommon = "libxkbcommon.so.0";
		private const int XkbKeymapFormatTextV1 = 1;

		[StructLayout(LayoutKind.Sequential)]
		private struct xkb_rule_names
		{
			public IntPtr rules;
			public IntPtr model;
			public IntPtr layout;
			public IntPtr variant;
			public IntPtr options;
		}

		[DllImport(LibXkbCommon)] private static extern IntPtr xkb_context_new(uint flags);
		[DllImport(LibXkbCommon)] private static extern void xkb_context_unref(IntPtr ctx);
		[DllImport(LibXkbCommon)] private static extern IntPtr xkb_keymap_new_from_names(IntPtr ctx, ref xkb_rule_names names, uint flags);
		[DllImport(LibXkbCommon)]
		private static extern IntPtr xkb_keymap_new_from_string(IntPtr ctx, [MarshalAs(UnmanagedType.LPUTF8Str)] string str, int format, uint flags);
		[DllImport(LibXkbCommon)] private static extern void xkb_keymap_unref(IntPtr keymap);
		[DllImport(LibXkbCommon)] private static extern int xkb_keymap_min_keycode(IntPtr keymap);
		[DllImport(LibXkbCommon)] private static extern int xkb_keymap_max_keycode(IntPtr keymap);
		[DllImport(LibXkbCommon)] private static extern uint xkb_keymap_mod_get_index(IntPtr keymap, string name);
		[DllImport(LibXkbCommon)] private static extern int xkb_keymap_num_layouts(IntPtr keymap);
		[DllImport(LibXkbCommon)] private static extern IntPtr xkb_keymap_layout_get_name(IntPtr keymap, uint idx);
		[DllImport(LibXkbCommon)] private static extern int xkb_keymap_num_layouts_for_key(IntPtr keymap, uint key);
		[DllImport(LibXkbCommon)] private static extern int xkb_keymap_num_levels_for_key(IntPtr keymap, uint key, uint layout);
		[DllImport(LibXkbCommon)] private static extern int xkb_keymap_key_get_syms_by_level(IntPtr keymap, uint key, uint layout, uint level, out IntPtr symsOut);
		[DllImport(LibXkbCommon, EntryPoint = "xkb_keymap_key_get_mods_for_level")]
		private static extern nuint xkb_keymap_key_get_mods_for_level(IntPtr keymap, uint key, uint layout, uint level, IntPtr masksOut, nuint masksOutSize);
		[DllImport(LibXkbCommon)] private static extern IntPtr xkb_keymap_key_get_name(IntPtr keymap, uint key);
		[DllImport(LibXkbCommon)] private static extern uint xkb_keysym_to_utf32(uint keysym);
	}
}
#endif
