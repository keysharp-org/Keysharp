using Keysharp.Builtins;
#if OSX
using System.Runtime.InteropServices;
using static Keysharp.Internals.Input.Keyboard.VirtualKeys;

namespace Keysharp.Internals.Input.Unix
{
	internal sealed partial class MacCharMapperProvider : IKeyCodeMapperProvider
	{
		private readonly Lock mapperLock = new();
		private readonly Dictionary<int, (uint vk, bool needShift, bool needAltGr)> cache = new();
		private nint retainedLayoutDataRef;
		private nint retainedLayoutPtr;
		private string retainedLayoutName = "";

		// Carbon HIToolbox modifier-key-state bits (used to select between candidate runes per key,
		// and passed directly as UCKeyTranslate's modifierKeyState parameter).
		private const uint shiftKeyState = 0x02u; // shiftKey >> 8
		private const uint optionKeyState = 0x08u; // optionKey >> 8
		private const uint alphaLockKeyState = 0x04u; // alphaLock (caps lock) >> 8

		private const ushort kUCKeyActionDown = 0;
		private const ushort kVK_Space = 0x31;

		// Dead-key composition state for TranslateKeyWithDeadKeys. UCKeyTranslate threads a
		// deadKeyState cookie across calls; we keep the "live" cookie plus the spacing form(s)
		// of any pending dead key(s) so a non-composing follow-up can emit e.g. "`z" rather
		// than dropping the accent.
		private uint liveDeadKeyState;
		private readonly StringBuilder pendingSpacing = new();

		private readonly Dictionary<uint, uint> vkToMacKeyCode = BuildVkToMacKeyCodeMap();

		private static readonly ushort[] textCandidateKeyCodes =
		{
			0x0A,
			0x00,0x01,0x02,0x03,0x04,0x05,0x06,0x07,0x08,0x09,0x0B,0x0C,0x0D,0x0E,0x0F,
			0x10,0x11,0x12,0x13,0x14,0x15,0x16,0x17,0x18,0x19,0x1A,0x1B,0x1C,0x1D,0x1E,0x1F,
			0x20,0x21,0x22,0x23,0x24,0x25,0x26,0x27,0x28,0x29,0x2A,0x2B,0x2C,0x2D,0x2E,0x2F,
			0x30,0x31,0x32
		};
		private static readonly uint[] textModifierStates =
			[0, shiftKeyState, optionKeyState, shiftKeyState | optionKeyState];

		private static Dictionary<uint, uint> BuildVkToMacKeyCodeMap()
		{
			var map = new Dictionary<uint, uint>();

			for (uint keyCode = 0; keyCode <= 0x7E; keyCode++)
			{
				if (!MapMacKeyCodeToVk(keyCode, out var vk))
					continue;

				// Preserve the first/primary mapping for duplicates like VK_RETURN:
				// 0x24 Return before 0x4C keypad Enter.
				map.TryAdd(vk, keyCode);
			}

			// MapMacKeyCodeToVk only produces the left/right-specific modifier VKs, so the neutral
			// VK_SHIFT/VK_CONTROL/VK_MENU are absent from the reverse map. Callers that synthesize a bare
			// modifier — e.g. the Alt-Tab hook action pressing VK_SHIFT to make ShiftAltTab move backward —
			// would otherwise map to nothing and be silently dropped (CreateKeyboardEvent returns Zero).
			// Alias each to its left-hand key, matching the Linux evdev mapping (VK_LSHIFT or VK_SHIFT => …).
			map.TryAdd(VK_SHIFT, 0x38);   // kVK_Shift (left)
			map.TryAdd(VK_CONTROL, 0x3B); // kVK_Control (left)
			map.TryAdd(VK_MENU, 0x3A);    // kVK_Option (left)

			return map;
		}

		private static readonly nint tisUnicodeLayoutKey = CreateCfString("TISPropertyUnicodeKeyLayoutData");
		private static readonly nint tisInputSourceIdKey = CreateCfString("TISPropertyInputSourceID");
		private static readonly nint tisLocalizedNameKey = CreateCfString("TISPropertyLocalizedName");
		private static readonly nint kTISNotifySelectedKeyboardInputSourceChanged = CreateCfString("TISNotifySelectedKeyboardInputSourceChanged");
		private const nint kCFNotificationSuspensionBehaviorDeliverImmediately = 4;

		private static readonly nint observerToken = 1;
		private static WeakReference<MacCharMapperProvider> layoutObserver;
		private WeakReference<Script> uiOwner;
		private bool monitoringStarted;
		private bool layoutPrepared;
		private volatile bool disposed;
		private int reloadRequests;

		public bool TryMapRuneToKeystroke(Rune rune, nint? layout, out uint vk, out bool needShift, out bool needAltGr)
		{
			// layout is unused on macOS: the current input source's layout data pointer is
			// already cached, so there is no per-character OS query to avoid.
			lock (mapperLock)
			{
				var key = rune.Value;

				var layoutPtr = retainedLayoutPtr;

				if (layoutPtr == nint.Zero)
				{
					needAltGr = false;
					return KeyCodes.TryMapAsciiToVk(rune, out vk, out needShift);
				}

				if (cache.TryGetValue(key, out var hit))
				{
					vk = hit.vk;
					needShift = hit.needShift;
					needAltGr = hit.needAltGr;
					return vk != 0;
				}

				// Prefer plain keys first (e.g. spacebar), then shifted, then AltGr combos.
				bool TryFindForModifiers(
					nint layoutPtr,
					Rune targetRune,
					uint modifiers,
					out uint foundVk)
				{
					foundVk = 0;

					foreach (var keyCode in textCandidateKeyCodes)
					{
						if (!TryTranslate(layoutPtr, keyCode, modifiers, out var translatedRune))
							continue;

						if (translatedRune.Value != targetRune.Value)
							continue;

						if (!TryMapMacKeyCodeToVk(keyCode, out foundVk))
							continue;

						return true;
					}

					return false;
				}

				foreach (var modifiers in textModifierStates)
				{
					if (TryFindForModifiers(layoutPtr, rune, modifiers, out vk))
					{
						needShift = (modifiers & shiftKeyState) != 0;
						needAltGr = (modifiers & optionKeyState) != 0;
						cache[key] = (vk, needShift, needAltGr);
						return true;
					}
				}

			}

			needAltGr = false;
			return KeyCodes.TryMapAsciiToVk(rune, out vk, out needShift);
		}

		public bool TryMapKeystrokeToRune(uint vk, bool shift, bool altGr, out Rune rune)
		{
			rune = default;
			lock (mapperLock)
			{
				if (!vkToMacKeyCode.TryGetValue(vk, out var keyCode))
					return false;

				var layoutPtr = retainedLayoutPtr;

				if (layoutPtr == nint.Zero)
					return false;

				uint modifiers = 0;

				if (shift)
					modifiers |= shiftKeyState;

				if (altGr)
					modifiers |= optionKeyState;

				return TryTranslate(layoutPtr, (ushort)keyCode, modifiers, out rune);
			}
		}

		public int TranslateKeyWithDeadKeys(uint vk, bool shift, bool altGr, bool capsLock, Span<char> buffer)
		{
			lock (mapperLock)
			{
				if (!vkToMacKeyCode.TryGetValue(vk, out var keyCode))
					return KeyCodes.TranslateNotHandled;

				var layoutPtr = retainedLayoutPtr;

				if (layoutPtr == nint.Zero)
					return KeyCodes.TranslateNotHandled;

				uint modifiers = 0;

				if (shift)
					modifiers |= shiftKeyState;

				if (altGr)
					modifiers |= optionKeyState;

				if (capsLock)
					modifiers |= alphaLockKeyState;

				// Backspace cancels any pending dead key (AHK's {dead key}{BS} behavior). Emit the
				// dead key's spacing form followed by '\b' so the caller collects then erases it,
				// yielding a net-zero change while clearing the pending composition.
				if (vk == VK_BACK && (liveDeadKeyState != 0 || pendingSpacing.Length > 0))
				{
					var n = WritePendingSpacing(buffer);

					if (n < buffer.Length)
						buffer[n++] = '\b';

					ResetDeadKeyStateCore();
					return n;
				}

				var hadPendingDeadKey = liveDeadKeyState != 0;
				uint ds = liveDeadKeyState;
				var (len, str) = UCKeyTranslateRaw(layoutPtr, (ushort)keyCode, modifiers, ref ds);

				if (len > 0)
				{
					if (hadPendingDeadKey)
					{
						// Determine whether the pending dead key actually combined with this key by
						// comparing against a fresh (no dead key) translation of the same keystroke.
						uint ds0 = 0;
						var (len0, str0) = UCKeyTranslateRaw(layoutPtr, (ushort)keyCode, modifiers, ref ds0);

						if (len0 == len && str0 == str) // No combination, e.g. dead-grave followed by 'z'.
						{
							var n = WritePendingSpacing(buffer);
							n += CopyToBuffer(str0, buffer[n..]);
							ResetDeadKeyStateCore();
							return n;
						}
					}

					// A normal character or a successful composition.
					liveDeadKeyState = ds; // Usually 0 now; preserved in case of chained state.
					pendingSpacing.Clear();
					return CopyToBuffer(str, buffer);
				}

				if (ds != 0) // This key is itself a (possibly chained) dead key.
				{
					liveDeadKeyState = ds;

					// Record its spacing form by translating Space against the new dead state (on a copy).
					uint dsSpace = ds;
					var (spLen, sp) = UCKeyTranslateRaw(layoutPtr, kVK_Space, 0, ref dsSpace);

					if (spLen > 0)
						_ = pendingSpacing.Append(sp);

					return -1;
				}

				// No text and not a dead key. With no pending composition, report "not handled" so the
				// caller falls back to the built-in US-ASCII map -- mirroring the X11 provider, which
				// returns TranslateNotHandled when it cannot resolve a keysym. This keeps basic keys
				// working when the active layout's UCKeyTranslate yields nothing (e.g. no usable Text
				// Input Source on a headless CI host). A genuine no-text key (arrow/modifier) isn't in
				// the US-ASCII table either, so the fallback still returns 0 for it.
				if (liveDeadKeyState == 0 && pendingSpacing.Length == 0)
					return KeyCodes.TranslateNotHandled;

				// A pending dead-key composition is intact; report no-text without disturbing it.
				return 0;
			}
		}

		public void ResetDeadKeyState()
		{
			lock (mapperLock)
				ResetDeadKeyStateCore();
		}

		private void ResetDeadKeyStateCore()
		{
			liveDeadKeyState = 0;
			_ = pendingSpacing.Clear();
		}

		private int WritePendingSpacing(Span<char> buffer)
		{
			var n = Math.Min(pendingSpacing.Length, buffer.Length);
			pendingSpacing.CopyTo(0, buffer, n);
			return n;
		}

		private static int CopyToBuffer(string s, Span<char> buffer)
		{
			var n = Math.Min(s.Length, buffer.Length);
			s.AsSpan(0, n).CopyTo(buffer);
			return n;
		}

		public void ConfigureLayout(string rules, string model, string layout, string variant, string options)
			=> QueueLayoutReload();

		private void InvalidateMappingsCore()
		{
			cache.Clear();
			ResetDeadKeyStateCore();
		}

		private void QueueLayoutReload()
		{
			if (disposed)
				return;
			if (Interlocked.Increment(ref reloadRequests) == 1)
			{
				var ownerRef = Volatile.Read(ref uiOwner);

				if (ownerRef == null || !ownerRef.TryGetTarget(out var owner) || owner.IsDisposed)
				{
					_ = Interlocked.Exchange(ref reloadRequests, 0);
					return;
				}

				_ = ThreadPool.QueueUserWorkItem(_ => owner.PostToUIThread(ProcessQueuedLayoutReload));
			}
		}

		private void ProcessQueuedLayoutReload()
		{
			while (true)
			{
				var requests = Volatile.Read(ref reloadRequests);
				if (disposed)
				{
					Interlocked.Exchange(ref reloadRequests, 0);
					return;
				}
				try { PublishCapturedLayout(CaptureCurrentLayout()); }
				catch (Exception ex) { Diagnostics.Debug.WriteLine($"macOS keyboard-layout refresh failed: {ex.Message}"); }
				if (Interlocked.CompareExchange(ref reloadRequests, 0, requests) == requests)
					return;
			}
		}

		public void PrepareForInputHook(Script owner)
		{
			ArgumentNullException.ThrowIfNull(owner);
			Volatile.Write(ref uiOwner, new WeakReference<Script>(owner));

			lock (mapperLock)
			{
				if (disposed || (layoutPrepared && monitoringStarted))
					return;
			}

			owner.InvokeOnUIThread(() =>
			{
				bool needsLayout;
				lock (mapperLock)
					needsLayout = !layoutPrepared && !disposed;

				if (needsLayout)
					PublishCapturedLayout(CaptureCurrentLayout());
				StartLayoutChangeMonitoringOnUiThread();
			});
		}

		private void StartLayoutChangeMonitoringOnUiThread()
		{
			lock (mapperLock)
			{
				if (monitoringStarted || disposed)
					return;
			}

			var registered = false;
			try
			{
				var center = CFNotificationCenterGetDistributedCenter();
				if (center != nint.Zero)
				{
					layoutObserver = new(this);
					unsafe
					{
						CFNotificationCenterAddObserver(
							center,
							observerToken,
							&OnSelectedKeyboardInputSourceChanged,
							kTISNotifySelectedKeyboardInputSourceChanged,
							nint.Zero,
							kCFNotificationSuspensionBehaviorDeliverImmediately);
					}
					registered = true;
				}
			}
			catch (Exception ex)
			{
				Diagnostics.Debug.WriteLine($"macOS keyboard-layout observer registration failed: {ex.Message}");
			}

			lock (mapperLock)
			{
				if (registered && !disposed)
				{
					monitoringStarted = true;
					return;
				}
			}

			layoutObserver = null;
			if (registered)
			{
				var center = CFNotificationCenterGetDistributedCenter();
				if (center != nint.Zero)
					CFNotificationCenterRemoveEveryObserver(center, observerToken);
			}
		}

		[UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
		private static void OnSelectedKeyboardInputSourceChanged(nint center, nint observer, nint name, nint obj, nint userInfo)
		{
			try
			{
				if (observer == observerToken && layoutObserver?.TryGetTarget(out var provider) == true)
					provider.QueueLayoutReload();
			}
			catch (Exception ex)
			{
				Diagnostics.Debug.WriteLine($"macOS keyboard-layout refresh failed: {ex.Message}");
			}
		}

		public nint GetCurrentKeymapHandle()
		{
			lock (mapperLock)
				return retainedLayoutPtr;
		}

		public string GetCurrentKeymapName()
		{
			lock (mapperLock)
				return retainedLayoutName;
		}

		public nint ResolveKeyboardLayout(string layout)
		{
			// TIS lookups are intentionally read-only here; selecting another input source would
			// mutate the user's active layout. Existing translation uses the current TIS layout.
			return GetCurrentKeymapHandle();
		}

		public void Dispose()
		{
			Volatile.Write(ref uiOwner, null);
			nint dataRef;
			lock (mapperLock)
			{
				if (disposed)
					return;
				disposed = true;
				InvalidateMappingsCore();
				dataRef = retainedLayoutDataRef;
				retainedLayoutDataRef = nint.Zero;
				retainedLayoutPtr = nint.Zero;
				retainedLayoutName = "";
				layoutPrepared = false;

				if (monitoringStarted)
					layoutObserver = null;
			}

			if (dataRef != nint.Zero)
				CFRelease(dataRef);

			if (!monitoringStarted)
				return;
			var center = CFNotificationCenterGetDistributedCenter();
			if (center != nint.Zero)
				CFNotificationCenterRemoveEveryObserver(center, observerToken);
		}

		public bool TryMapVkToMacKeyCode(uint vk, out uint keyCode) => vkToMacKeyCode.TryGetValue(vk, out keyCode);

		public bool TryMapMacKeyCodeToVk(uint keyCode, out uint vk) => MapMacKeyCodeToVk(keyCode, out vk);

		public static bool MapMacKeyCodeToVk(uint keyCode, out uint vk)
		{
			vk = keyCode switch
			{
				// ANSI letter keys
				0x00 => (uint)'A',
				0x01 => (uint)'S',
				0x02 => (uint)'D',
				0x03 => (uint)'F',
				0x04 => (uint)'H',
				0x05 => (uint)'G',
				0x06 => (uint)'Z',
				0x07 => (uint)'X',
				0x08 => (uint)'C',
				0x09 => (uint)'V',
				0x0B => (uint)'B',
				0x0C => (uint)'Q',
				0x0D => (uint)'W',
				0x0E => (uint)'E',
				0x0F => (uint)'R',
				0x10 => (uint)'Y',
				0x11 => (uint)'T',
				0x1F => (uint)'O',
				0x20 => (uint)'U',
				0x22 => (uint)'I',
				0x23 => (uint)'P',
				0x25 => (uint)'L',
				0x26 => (uint)'J',
				0x28 => (uint)'K',
				0x2D => (uint)'N',
				0x2E => (uint)'M',

				// ANSI number row
				0x12 => (uint)'1',
				0x13 => (uint)'2',
				0x14 => (uint)'3',
				0x15 => (uint)'4',
				0x16 => (uint)'6',
				0x17 => (uint)'5',
				0x19 => (uint)'9',
				0x1A => (uint)'7',
				0x1C => (uint)'8',
				0x1D => (uint)'0',

				// ANSI punctuation / OEM keys
				0x0A => VK_OEM_102,    // kVK_ISO_Section, ISO extra key: <> / §± / \| depending on layout
				0x18 => VK_OEM_PLUS,   // =
				0x1B => VK_OEM_MINUS,  // -
				0x1E => VK_OEM_6,      // ]
				0x21 => VK_OEM_4,      // [
				0x27 => VK_OEM_7,      // '
				0x29 => VK_OEM_1,      // ;
				0x2A => VK_OEM_5,      // \
				0x2B => VK_OEM_COMMA,  // ,
				0x2C => VK_OEM_2,      // /
				0x2F => VK_OEM_PERIOD, // .
				0x32 => VK_OEM_3,      // `

				// Main control keys
				0x24 => VK_RETURN,     // Return
				0x30 => VK_TAB,
				0x31 => VK_SPACE,
				0x33 => VK_BACK,       // macOS Delete == Windows Backspace
				0x35 => VK_ESCAPE,
				0x39 => VK_CAPITAL,    // Caps Lock

				// Modifiers
				0x38 => VK_LSHIFT,
				0x3C => VK_RSHIFT,

				// macOS Control maps to Windows Ctrl
				0x3B => VK_LCONTROL,
				0x3E => VK_RCONTROL,

				// macOS Option maps to Windows Alt/Menu
				0x3A => VK_LMENU,
				0x3D => VK_RMENU,

				// macOS Command maps to Windows Win/Super
				0x37 => VK_LWIN,
				0x36 => VK_RWIN,

				// Function keys
				0x7A => VK_F1,
				0x78 => VK_F2,
				0x63 => VK_F3,
				0x76 => VK_F4,
				0x60 => VK_F5,
				0x61 => VK_F6,
				0x62 => VK_F7,
				0x64 => VK_F8,
				0x65 => VK_F9,
				0x6D => VK_F10,
				0x67 => VK_F11,
				0x6F => VK_F12,
				0x69 => VK_F13,
				0x6B => VK_F14,
				0x71 => VK_F15,
				0x6A => VK_F16,
				0x40 => VK_F17,
				0x4F => VK_F18,
				0x50 => VK_F19,
				0x5A => VK_F20,

				// Navigation / editing
				0x72 => VK_HELP,       // kVK_Help
				0x73 => VK_HOME,
				0x74 => VK_PRIOR,      // Page Up
				0x75 => VK_DELETE,     // Forward Delete
				0x77 => VK_END,
				0x79 => VK_NEXT,       // Page Down
				0x7B => VK_LEFT,
				0x7C => VK_RIGHT,
				0x7D => VK_DOWN,
				0x7E => VK_UP,

				// Keypad
				0x41 => VK_DECIMAL,    // keypad .
				0x43 => VK_MULTIPLY,
				0x45 => VK_ADD,
				0x47 => VK_CLEAR,      // keypad clear; closest to NumLock-ish behavior on Mac
				0x4B => VK_DIVIDE,
				0x4C => VK_RETURN,     // keypad Enter; distinguish via scan/keyCode if needed
				0x4E => VK_SUBTRACT,
				0x51 => VK_OEM_PLUS,   // keypad =
				0x52 => VK_NUMPAD0,
				0x53 => VK_NUMPAD1,
				0x54 => VK_NUMPAD2,
				0x55 => VK_NUMPAD3,
				0x56 => VK_NUMPAD4,
				0x57 => VK_NUMPAD5,
				0x58 => VK_NUMPAD6,
				0x59 => VK_NUMPAD7,
				0x5B => VK_NUMPAD8,
				0x5C => VK_NUMPAD9,

				// Volume keys
				0x48 => VK_VOLUME_UP,
				0x49 => VK_VOLUME_DOWN,
				0x4A => VK_VOLUME_MUTE,

				// JIS-specific keys.
				// These do not have great Windows VK equivalents, but these are the least-bad mappings.
				0x5D => VK_OEM_5,      // kVK_JIS_Yen
				0x5E => VK_OEM_102,    // kVK_JIS_Underscore / Ro
				0x5F => VK_SEPARATOR,  // kVK_JIS_KeypadComma
				0x66 => VK_NONCONVERT,      // kVK_JIS_Eisu
				0x68 => VK_KANA,      // kVK_JIS_Kana
				0x6E => VK_APPS,      // kVK_ContextualMenu

				_ => 0u
			};

			return vk != 0;
		}

		/// <summary>
		/// Resolves the character that pressing a key (with the given Carbon modifier-key
		/// state, e.g. <see cref="shiftKeyState"/>/<see cref="optionKeyState"/>) would produce
		/// under the given keyboard layout's Unicode key layout data, via UCKeyTranslate.
		/// Returns false for dead keys (no completed character without a follow-up keystroke).
		/// </summary>
		private static bool TryTranslate(nint layoutPtr, ushort keyCode, uint modifierState, out Rune rune)
		{
			uint deadKeyState = 0;
			var (len, text) = UCKeyTranslateRaw(layoutPtr, keyCode, modifierState, ref deadKeyState);
			rune = default;
			return len > 0 && Rune.TryGetRuneAt(text, 0, out rune);
		}

		/// <summary>
		/// Low-level UCKeyTranslate wrapper that threads a dead-key state cookie. Returns the number
		/// of UTF-16 chars produced (0 for a dead key or a key with no text) and the produced string.
		/// On return, <paramref name="deadKeyState"/> is non-zero if a dead key is now buffered.
		/// </summary>
		private static unsafe (int len, string str) UCKeyTranslateRaw(nint layoutPtr, ushort keyCode, uint modifierState, ref uint deadKeyState)
		{
			if (layoutPtr == nint.Zero)
				return (0, string.Empty);

			const int bufferLen = 8;
			var chars = stackalloc ushort[bufferLen];
			uint ds = deadKeyState;

			var status = UCKeyTranslate(
				layoutPtr,
				keyCode,
				kUCKeyActionDown,
				modifierState,
				0,
				0,
				&ds,
				bufferLen,
				out var actualLength,
				chars);

			deadKeyState = ds;

			if (status != 0)
				return (0, string.Empty);

			var len = (int)Math.Min(actualLength, bufferLen);

			if (len <= 0)
				return (0, string.Empty);

			var translated = new string((char*)chars, 0, len);

			if (translated.Length == 1 && translated[0] == '�')
				return (0, string.Empty);

			return (len, translated);
		}

		private readonly record struct CapturedLayout(bool HasSource, nint DataRef, nint LayoutPtr, string Name);

		private static CapturedLayout CaptureCurrentLayout()
		{
			// Prefer the actual current input source so non-ASCII characters can be translated. 
			// The ASCII-capable source is only a fallback, since it represents a US-like layout 
			// used for keyboard shortcuts and would never contain non-ASCII characters.
			var source = TISCopyCurrentKeyboardLayoutInputSource();

			if (source == nint.Zero)
				source = TISCopyCurrentASCIICapableKeyboardLayoutInputSource();

			if (source == nint.Zero)
				return default;

			try
			{
				var name = GetInputSourceName(source);
				var keyRef = tisUnicodeLayoutKey;
				if (keyRef == nint.Zero)
					return new(true, nint.Zero, nint.Zero, name);

				var dataRef = TISGetInputSourceProperty(source, keyRef);
				if (dataRef == nint.Zero)
					return new(true, nint.Zero, nint.Zero, name);

				// Guard against invalid native values before dereferencing CFData.
				if (CFGetTypeID(dataRef) != CFDataGetTypeID())
					return new(true, nint.Zero, nint.Zero, name);

				var ptr = CFDataGetBytePtr(dataRef);
				if (ptr == nint.Zero)
					return new(true, nint.Zero, nint.Zero, name);

				CFRetain(dataRef);
				return new(true, dataRef, ptr, name);
			}
			finally
			{
				CFRelease(source);
			}
		}

		private void PublishCapturedLayout(CapturedLayout captured)
		{
			// A transient failure to obtain any input source preserves the last complete snapshot;
			// explicit sources without Unicode layout data publish the zero-pointer fallback.
			if (!captured.HasSource)
				return;

			nint dataRefToRelease;
			lock (mapperLock)
			{
				if (disposed)
				{
					dataRefToRelease = captured.DataRef;
				}
				else
				{
					dataRefToRelease = retainedLayoutDataRef;
					retainedLayoutDataRef = captured.DataRef;
					retainedLayoutPtr = captured.LayoutPtr;
					retainedLayoutName = captured.Name;
					layoutPrepared = true;
					InvalidateMappingsCore();
				}
			}

			// Translation holds mapperLock for the full UCKeyTranslate call, so once the swap
			// above completes no callback can still be dereferencing the retired CFData bytes.
			if (dataRefToRelease != nint.Zero)
				CFRelease(dataRefToRelease);
		}

		private static string GetInputSourceName(nint source)
		{
			var id = GetInputSourcePropertyString(source, tisInputSourceIdKey);
			var name = GetInputSourcePropertyString(source, tisLocalizedNameKey);

			if (id != "" && name != "")
				return $"{id}:{name}";

			return id != "" ? id : name;
		}

		private static string GetInputSourcePropertyString(nint source, nint propertyKey)
		{
			if (propertyKey == nint.Zero)
				return "";

			var value = TISGetInputSourceProperty(source, propertyKey);
			return CopyCfString(value);
		}

		private static string CopyCfString(nint stringRef)
		{
			if (stringRef == nint.Zero || CFGetTypeID(stringRef) != CFStringGetTypeID())
				return "";

			var len = CFStringGetLength(stringRef);
			var maxSize = CFStringGetMaximumSizeForEncoding(len, kCFStringEncodingUTF8) + 1;

			if (maxSize <= 1 || maxSize > int.MaxValue)
				return "";

			var buffer = new byte[(int)maxSize];

			if (!CFStringGetCString(stringRef, buffer, maxSize, kCFStringEncodingUTF8))
				return "";

			var nul = System.Array.IndexOf(buffer, (byte)0);
			return Encoding.UTF8.GetString(buffer, 0, nul >= 0 ? nul : buffer.Length);
		}

		private static nint CreateCfString(string value) =>
			CFStringCreateWithCString(nint.Zero, value, kCFStringEncodingUTF8);

		private const uint kCFStringEncodingUTF8 = 0x08000100;

		[LibraryImport("/System/Library/Frameworks/Carbon.framework/Carbon")]
		private static partial nint TISCopyCurrentKeyboardLayoutInputSource();

		[LibraryImport("/System/Library/Frameworks/Carbon.framework/Carbon")]
		private static partial nint TISCopyCurrentASCIICapableKeyboardLayoutInputSource();

		[LibraryImport("/System/Library/Frameworks/Carbon.framework/Carbon")]
		private static partial nint TISGetInputSourceProperty(nint inputSource, nint propertyKey);

		[LibraryImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
		private static partial void CFRelease(nint cfTypeRef);

		[LibraryImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
		private static partial nint CFRetain(nint cfTypeRef);

		[LibraryImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
		private static partial nint CFGetTypeID(nint cf);

		[LibraryImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
		private static partial nint CFDataGetTypeID();

		[LibraryImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation", StringMarshalling = StringMarshalling.Utf8)]
		private static partial nint CFStringCreateWithCString(nint alloc, string cStr, uint encoding);

		[LibraryImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
		private static partial nint CFDataGetBytePtr(nint theData);

		[LibraryImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
		private static partial nint CFStringGetTypeID();

		[LibraryImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
		private static partial nint CFStringGetLength(nint theString);

		[LibraryImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
		private static partial nint CFStringGetMaximumSizeForEncoding(nint length, uint encoding);

		[LibraryImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static partial bool CFStringGetCString(nint theString, byte[] buffer, nint bufferSize, uint encoding);

		[LibraryImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
		private static partial nint CFNotificationCenterGetDistributedCenter();

		[DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
		private static unsafe extern void CFNotificationCenterAddObserver(
			nint center,
			nint observer,
			delegate* unmanaged[Cdecl]<nint, nint, nint, nint, nint, void> callBack,
			nint name,
			nint object_,
			nint suspensionBehavior);

		[DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
		private static extern void CFNotificationCenterRemoveEveryObserver(nint center, nint observer);

		[DllImport("/System/Library/Frameworks/Carbon.framework/Carbon")]
		private static extern unsafe int UCKeyTranslate(
			nint keyLayoutPtr,
			ushort virtualKeyCode,
			ushort keyAction,
			uint modifierKeyState,
			uint keyboardType,
			uint keyTranslateOptions,
			uint* deadKeyState,
			uint maxStringLength,
			out uint actualStringLength,
			ushort* unicodeString);
	}
}
#endif
