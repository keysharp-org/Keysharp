#if LINUX
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using SharpHook;
using SharpHook.Data;
using static Keysharp.Core.Common.Keyboard.KeyboardUtils;
using static Keysharp.Core.Common.Keyboard.VirtualKeys;
using static Keysharp.Core.Linux.SharpHookKeyMapper;
using static Keysharp.Core.Common.Keyboard.KeyboardMouseSender;
using static Keysharp.Core.Linux.X11.Xlib;
using static Keysharp.Core.Linux.PlatformManager;

namespace Keysharp.Core.Linux
{
	/// <summary>
	/// Linux implementation of HookThread.
	/// Listens with SharpHook; uses XGrabKey for hotkeys which must be blocked (no tilde).
	/// </summary>
	internal class LinuxHookThread : Keysharp.Core.Common.Threading.HookThread
	{
		private static readonly bool HookDisabled = ShouldDisableHook();
		[Conditional("DEBUG")]
		private static void DebugLog(string message) => Console.WriteLine(message);

		// --- SharpHook ---
		private SimpleGlobalHook? globalHook;
		private Task? hookRunTask;

		private readonly Lock hookStateLock = new();

		// Cached on/off
		private volatile bool keyboardEnabled;
		private volatile bool mouseEnabled;

		// Custom modifier tracking (e.g. CapsLock & a)
		private CustomPrefixState customPrefix;
		private IndicatorSnapshot indicatorSnapshot = new(false, false, false);

		private readonly struct IndicatorSnapshot
		{
			public readonly bool Caps;
			public readonly bool Num;
			public readonly bool Scroll;
			public IndicatorSnapshot(bool caps, bool num, bool scroll)
			{
				Caps = caps; Num = num; Scroll = scroll;
			}
		}

		private readonly Dictionary<uint, bool> customPrefixSuppress = new();
		private readonly Dictionary<uint, List<(uint keycode, uint mods)>> dynamicPrefixGrabs = new();
		private uint activeHotkeyVk;
		private KeyCode activeHotkeyKc;
		private bool activeHotkeyDown;
		private readonly byte[] logicalKeyState = new byte[VK_ARRAY_COUNT];
		internal uint ActiveHotkeyVk => activeHotkeyVk;
		internal KeyCode ActiveHotkeyKc => activeHotkeyKc;
		internal bool SendInProgress => sendInProgress;
		internal bool IsHotkeySuffixDown(uint vk) => activeHotkeyDown && activeHotkeyVk == vk;

		internal readonly struct SyntheticToken
		{
			internal readonly DateTimeOffset EnqueueTime;   // TickCount64-based
			internal readonly KeyCode KeyCode;
			internal readonly bool KeyUp;
			internal readonly long ExtraInfo;     // what you want hook to see
			internal SyntheticToken(KeyCode keyCode, bool keyUp, DateTimeOffset ms, long extraInfo)
			{
				KeyCode = keyCode;
				KeyUp = keyUp;
				EnqueueTime = ms;
				ExtraInfo = extraInfo;
			}
		}

		private readonly List<SyntheticToken> pendingSynthetic = new();
		internal const long SyntheticEventTimeoutMs = 200;
		private readonly Dictionary<uint, int> suppressedHotkeyReleases = new();
		private char lastTypedChar;
		private uint lastKeyboardEventVk;
		private bool lastHookEventWasKeyboard;


		// Hotstring press-time trigger helpers
		private ulong lastTypedExtraInfo = (ulong)(SendLevelMax + 1); // tracks last KeyTyped extra info for InputLevel checks

		private static CaseConformModes ComputeCaseMode(HotstringDefinition hs, List<char> buf)
		{
			if (!hs.conformToCase || buf.Count == 0)
				return CaseConformModes.None;

			var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(buf);
			int end = span.Length - (hs.endCharRequired ? 1 : 0);
			if (end <= 0)
				return CaseConformModes.None;

			int start = Math.Max(0, end - hs.str.Length);
			int cap = 0, up = 0;

			for (int i = start; i < end; i++)
			{
				char c = span[i];
				if (char.IsLetter(c)) { cap++; if (char.IsUpper(c)) up++; }
			}

			if (cap == 0)
				return CaseConformModes.None;

			if (up == cap)
				return CaseConformModes.AllCaps;
			if (up == 0)
				return CaseConformModes.None;
			if (char.IsLetter(span[start]) && char.IsUpper(span[start]))
				return CaseConformModes.FirstCap;

			return CaseConformModes.None;
		}

		// Hotstring arming state
		private bool hsArmed;   // any end-keys are armed right now?

		// Each armed end-key we grab (base grab; variants for Caps/NumLock are stored in hsActiveGrabVariants)
		private readonly HashSet<ArmedEnd> hsArmedEnds = new();

		private readonly HashSet<(uint keycode, uint mods)> hsActiveGrabVariants = new(); // to ungrab fast

		private struct ArmedEnd
		{
			public uint Keycode;             // X11 keycode we grabbed
			public uint XModsBase;          // base X11 modifiers used for grab (without Lock/NumLock variants)
			public uint Vk;                 // engine VK for that end key
			public char EndChar;            // actual end character we will append to the hs buffer
			public bool NeedShift;          // required to produce EndChar
			public bool NeedAltGr;          // required to produce EndChar (ISO_Level3 / Mod5)
		}

		private enum LockKeyKind
		{
			None,
			CapsLock,
			NumLock,
			ScrollLock
		}

		private struct CustomPrefixState
		{
			public uint Vk;
			public bool Suppressed;
			public LockKeyKind LockKind;
			public bool LockState;
			public bool LockCaptured;
			public bool Used;           // any other key pressed while held?
			public bool FireOnRelease;  // whether to fire the prefix hotkey on release
			public void Reset()
			{
				Vk = 0;
				Suppressed = false;
				LockKind = LockKeyKind.None;
				LockState = false;
				LockCaptured = false;
				Used = false;
				FireOnRelease = false;
			}
		}

		// --- Simple hotkey map for Linux (vk + LR-mods, up/down) ---
		internal readonly Lock hkLock = new();
		private readonly List<LinuxHotkey> linuxHotkeys = new(); // minimal matcher

		private struct LinuxHotkey
		{
			public uint IdWithFlags;
			public uint Vk;
			public uint ModifiersLR; // LR-specific mask (MOD_* bits)
			public uint ModifierVK;   // custom prefix VK (0 if none)
			public bool KeyUp;       // is a key-up hotkey
			public bool PassThrough; // ~ tilde present => don't grab
			public bool AllowExtra;  // wildcard (*) hotkey: allow extra modifiers
		}

		// Tracks plain hotkeys grabbed via XGrabKey as well as dynamic grabs for custom prefixes.
		private readonly HashSet<(uint xCode, uint mods)> activeGrabs = new();
		private readonly HashSet<(uint xCode, uint mods)> activeHotstringGrabs = new();
		private bool sendInProgress;
		private int sendDepth;
		internal readonly struct SendScope : IDisposable
		{
			private readonly LinuxHookThread owner;
			public SendScope(LinuxHookThread owner)
			{
				this.owner = owner;
				owner.sendDepth++;
				owner.sendInProgress = true;
			}

			public void Dispose()
			{
				owner.sendDepth = Math.Max(0, owner.sendDepth - 1);
				owner.sendInProgress = owner.sendDepth > 0;
				if (!owner.sendInProgress)
					owner.SyncModifiersAfterSend();
			}
		}
		internal struct GrabSnapshot
		{
			public bool Active;
			public HashSet<(uint xcode, uint mods)> Grabs;
			public HashSet<(uint xcode, uint mods)> HotstringGrabs;
		}

		internal LinuxHookThread()
		{
			kbdMsSender = new LinuxKeyboardMouseSender();
		}

		internal SendScope EnterSendScope() => new(this);

		private void SyncModifiersAfterSend()
		{
			// Ensure modifiers don't stay logically down after Send manipulates them.
			_ = kbdMsSender?.GetModifierLRState(true);
		}

		// -------------------- lifecycle --------------------
		protected internal override void DeregisterHooks()
		{
			StopGlobalHook();
			UngrabAll();
		}

		public override void SimulateKeyPress(uint key)
			=> kbdMsSender.SendKeyEvent(KeyEventTypes.KeyDownAndUp, key, 0, 0, false, 0);

		// -------------------- enable/disable --------------------

		internal override void AddRemoveHooks(HookType hooksToBeActive, bool changeIsTemporary = false)
		{
			ChangeHookStateLinux(hooksToBeActive & (HookType.Keyboard | HookType.Mouse), changeIsTemporary);
		}

		private void ChangeHookStateLinux(HookType req, bool changeIsTemporary)
		{
			if (HookDisabled)
			{
				lock (hookStateLock)
				{
					keyboardEnabled = false;
					mouseEnabled = false;
					kbdHook = 0;
					mouseHook = 0;

					// permanent stop if hook is disabled via env var
					StopGlobalHookCore(dispose: true);
				}

				KeysharpEnhancements.OutputDebugLine("Linux hook disabled via KEYSHARP_DISABLE_HOOK=1.");
				return;
			}

			bool wantKeyboard = (req & HookType.Keyboard) != 0;
			bool wantMouse    = (req & HookType.Mouse) != 0;

			lock (hookStateLock)
			{
				// Remember previous "caller wants it" state (since these bools are now dual-purpose).
				bool hadKeyboard = keyboardEnabled;
				bool hadMouse    = mouseEnabled;

				// ---- transition gate OFF (prevents racing init / mid-transition events) ----


				// If nothing requested:
				if (!wantKeyboard && !wantMouse)
				{
					keyboardEnabled = false; kbdHook = 0;
					mouseEnabled = false; mouseHook = 0;
					
					if (changeIsTemporary)
					{
						// Keep SimpleGlobalHook running, just stop processing events.
						// (Do NOT reset state here; we’ll resync on re-enable.)
						return;
					}

					// Permanent removal: stop + clear.
					StopGlobalHookCore(dispose: true);
					return;
				}

				// Ensure the hook is running (only start/attach once).
				if (globalHook == null || !globalHook.IsRunning)
				{
					StopGlobalHookCore(dispose: true); // clean restart if something half-exists

					globalHook = new SimpleGlobalHook();

					globalHook.KeyPressed += OnKeyPressed;
					globalHook.KeyReleased += OnKeyReleased;

					globalHook.MousePressed += OnMousePressed;
					globalHook.MouseReleased += OnMouseReleased;
					globalHook.MouseMoved += OnMouseMoved;
					globalHook.MouseWheel += OnMouseWheel;

					// Start hook thread
					hookRunTask = globalHook.RunAsync();
				}

				// Respect Windows semantics: ResetHook only for non-temporary transitions
				// and only when something is newly enabled.
				if (!changeIsTemporary)
				{
					if (wantKeyboard && !hadKeyboard)
					{
						keyboardEnabled = false; kbdHook = 0;
						// Always resync snapshots right before enabling processing.
						// This prevents “stuck key”/wrong modifier state after temporary disable,
						// and also ensures no stale state after restart.
						InitSnapshotFromX11();
						ResetHook(false, HookType.Keyboard, true);
					}
					if (wantMouse && !hadMouse)
					{
						mouseEnabled = false; mouseHook = 0;
						ResetHook(false, HookType.Mouse, true);
					}
				}

				// ---- transition complete: enable processing AND indicate desired state ----
				keyboardEnabled = wantKeyboard;
				mouseEnabled = wantMouse;

				kbdHook = wantKeyboard ? 1 : 0;   // sentinel for HasKbdHook()
				mouseHook = wantMouse ? 1 : 0;
			}
		}

		private void StopGlobalHookCore(bool dispose)
		{
			// Must be called under hookStateLock.

			if (dispose)
			{
				try { globalHook?.Dispose(); } catch { }
				try { if (hookRunTask != null && !hookRunTask.IsCompleted) hookRunTask.Wait(50); } catch { }
				globalHook = null;
				hookRunTask = null;

				// When permanently disposing, clear state like before.
				System.Array.Clear(physicalKeyState, 0, physicalKeyState.Length);
				System.Array.Clear(logicalKeyState, 0, logicalKeyState.Length);
				kbdMsSender.modifiersLRLogical = 0;
				kbdMsSender.modifiersLRLogicalNonIgnored = 0;
				kbdMsSender.modifiersLRPhysical = 0;
			}
			// If dispose==false: we intentionally keep the hook alive and keep current
			// state around; the processing gate is controlled by keyboardEnabled/mouseEnabled.
		}


		internal override void ChangeHookState(List<HotkeyDefinition> hk, HookType whichHook, HookType whichHookAlways)
		{
			base.ChangeHookState(hk, whichHook, whichHookAlways);

			// Rebuild minimal hotkey matcher + X11 grabs for blocking hotkeys:
			lock (hkLock)
			{
				linuxHotkeys.Clear();
				customPrefixSuppress.Clear();
				dynamicPrefixGrabs.Clear();
				UngrabAll();

				if (hk != null)
				{
					foreach (var def in hk)
					{
						var entry = new LinuxHotkey
						{
							IdWithFlags = def.keyUp ? (def.id | HotkeyDefinition.HOTKEY_KEY_UP) : def.id,
							Vk = def.vk,
							ModifiersLR = def.modifiersConsolidatedLR,
							ModifierVK = def.modifierVK,
							KeyUp = def.keyUp,
							PassThrough = (def.noSuppress & HotkeyDefinition.AT_LEAST_ONE_VARIANT_HAS_TILDE) != 0,
							AllowExtra = def.allowExtraModifiers
						};
						linuxHotkeys.Add(entry);

						if (def.modifierVK != 0)
						{
							// By default, a custom prefix is suppressed unless the tilde prefix was used.
							var suppressPrefix = (def.noSuppress & HotkeyDefinition.NO_SUPPRESS_PREFIX) == 0;

							if (customPrefixSuppress.TryGetValue(def.modifierVK, out var suppressExisting))
								customPrefixSuppress[def.modifierVK] = suppressExisting || suppressPrefix;
							else
								customPrefixSuppress[def.modifierVK] = suppressPrefix;
						}

						// XGrabKey only when we must block (no tilde), and only if X11 is available.
						if (!entry.PassThrough && IsX11Available && entry.Vk != 0)
						{
							var modsForGrab = entry.ModifiersLR;
							bool? neutral = null;
							if (entry.ModifierVK != 0)
								modsForGrab |= KeyToModifiersLR(entry.ModifierVK, 0, ref neutral);

							// For custom prefixes that aren't real modifiers, grab the suffix only while the prefix is held.
							if (entry.ModifierVK != 0 && modsForGrab == 0)
							{
								if (TryMapToXGrab(entry.Vk, modsForGrab, out var keycode, out var mods))
								{
									if (!dynamicPrefixGrabs.TryGetValue(entry.ModifierVK, out var list))
										dynamicPrefixGrabs[entry.ModifierVK] = list = new();
									list.Add((keycode, mods));
								}
							}
							else if (TryMapToXGrab(entry.Vk, modsForGrab, out var keycode, out var mods))
							{
								if (entry.AllowExtra)
									GrabKeyWithExtraModifiers(keycode, mods);
								else
									GrabKey(keycode, mods);
							}
						}
					}

					if (IsX11Available)
					{
						foreach (var kvp in customPrefixSuppress)
						{
							if (!kvp.Value) continue; // tilde allows the prefix to pass through
							if (TryMapToXGrab(kvp.Key, 0, out var prefixKeycode, out _))
								GrabKeyVariantsInto(prefixKeycode, AnyModifier, activeGrabs, anyModifier: true);
						}
					}
				}
			}
		}

		internal override void Unhook() => DeregisterHooks();
		internal override void Unhook(nint hook) => DeregisterHooks();

		private void InitSnapshotFromX11()
		{
			if (!TryQueryKeymap(out var km))
				return;

			// Clear first
			System.Array.Clear(physicalKeyState, 0, physicalKeyState.Length);
			System.Array.Clear(logicalKeyState, 0, logicalKeyState.Length);

			// Populate physicalKeyState + logicalKeyState from the keymap bits
			for (int keycode = 8; keycode < 256; keycode++)
			{
				var byteIndex = keycode >> 3;
				var bitMask = 1 << (keycode & 7);
				if ((km[byteIndex] & bitMask) == 0) continue;

				var ks = XDisplay.Default.XKeycodeToKeysym(keycode, 0);
				if (ks == 0) continue;

				var vk = VkFromKeysym((ulong)ks);
				if (vk != 0 && vk < physicalKeyState.Length)
				{
					physicalKeyState[vk] = StateDown;
					logicalKeyState[vk] = StateDown;
				}
			}

			// Derive modifiers from keymap for logical masks
			if (TryQueryModifierLRState(out var modsInit, km))
			{
				kbdMsSender.modifiersLRPhysical = modsInit;
				kbdMsSender.modifiersLRLogical = modsInit;
				kbdMsSender.modifiersLRLogicalNonIgnored = modsInit;

				void SetLogical(uint modMask, uint vk)
				{
					if ((modsInit & modMask) != 0 && vk < logicalKeyState.Length)
						logicalKeyState[vk] = StateDown;
				}

				SetLogical(MOD_LSHIFT,   VK_LSHIFT);   SetLogical(MOD_RSHIFT,   VK_RSHIFT);
				SetLogical(MOD_LCONTROL, VK_LCONTROL); SetLogical(MOD_RCONTROL, VK_RCONTROL);
				SetLogical(MOD_LALT,     VK_LMENU);    SetLogical(MOD_RALT,     VK_RMENU);
				SetLogical(MOD_LWIN,     VK_LWIN);     SetLogical(MOD_RWIN,     VK_RWIN);
			}

			_ = RefreshIndicatorSnapshot();
		}

		private void StopGlobalHook()
		{
			try { globalHook?.Dispose(); } catch { }
			try { if (hookRunTask != null && !hookRunTask.IsCompleted) hookRunTask.Wait(50); } catch { }
			globalHook = null;
			hookRunTask = null;

			System.Array.Clear(physicalKeyState, 0, physicalKeyState.Length);
			System.Array.Clear(logicalKeyState, 0, logicalKeyState.Length);
			kbdMsSender.modifiersLRLogical = 0;
			kbdMsSender.modifiersLRLogicalNonIgnored = 0;
			kbdMsSender.modifiersLRPhysical = 0;
			kbdHook = 0;
			mouseHook = 0;
		}

		private static bool ShouldDisableHook()
		{
			var env = Environment.GetEnvironmentVariable("KEYSHARP_DISABLE_HOOK");
			return !string.IsNullOrEmpty(env) &&
				   (env.Equals("1") || env.Equals("true", StringComparison.OrdinalIgnoreCase) || env.Equals("yes", StringComparison.OrdinalIgnoreCase));
		}

		// -------------------- event handlers --------------------

		private void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
		{
			if (!keyboardEnabled) return;

			var keyCode = e.Data.KeyCode;
			var vk = KeyCodeToVk(keyCode);
			if (vk == 0) return;

			lastHookEventWasKeyboard = true;
			lastKeyboardEventVk = vk;
			var sc = (uint)e.RawEvent.Keyboard.RawCode;
			var wasGrabbed = WasKeyGrabbed(e, vk, out var grabbedByHotstring);
			var isInjected = MarkSimulatedIfNeeded(e, vk, keyCode, false, out ulong extraInfo);

			// Track logical state as seen by apps.
			UpdateLogicalKeyFromHook(vk, keyUp: false, wasGrabbed);

			if (!isInjected && grabbedByHotstring)
				ForceReleaseEndKeyX11(vk);

			DebugLog($"[Hook] KeyDown vk={vk} sc={sc} grabbed={wasGrabbed} hsGrab={grabbedByHotstring} simulated={isInjected} extraInfo={extraInfo}");

			var result = LowLevelCommon(e, vk, sc, sc, keyUp: false, extraInfo, (uint)(isInjected ? 0x10 : 0));

			if (result == 0 && !isInjected && wasGrabbed && !grabbedByHotstring)
				ReplayGrabbedKey(keyCode, vk, sc, false);
		}

		private void OnKeyReleased(object? sender, KeyboardHookEventArgs e)
		{
			if (!keyboardEnabled) return;

			var keyCode = e.Data.KeyCode;
			var vk = KeyCodeToVk(keyCode);
			if (vk == 0) return;

			lastHookEventWasKeyboard = true;
			lastKeyboardEventVk = vk;
			var sc = (uint)e.RawEvent.Keyboard.RawCode;
			var wasGrabbed = WasKeyGrabbed(e, vk, out var grabbedByHotstring);
			var isInjected = MarkSimulatedIfNeeded(e, vk, keyCode, true, out ulong extraInfo);

			UpdateLogicalKeyFromHook(vk, keyUp: true, wasGrabbed);

			DebugLog($"[Hook] KeyUp vk={vk} sc={sc} grabbed={wasGrabbed} hsGrab={grabbedByHotstring} simulated={isInjected}");

			var result = LowLevelCommon(e, vk, sc, sc, keyUp: true, extraInfo, (uint)(isInjected ? 0x10 : 0));

			if (result == 0 && !isInjected && wasGrabbed && !grabbedByHotstring)
				ReplayGrabbedKey(keyCode, vk, sc, true);
		}

		// On Linux we derive typed characters during EarlyCollectInput; no KeyTyped handler needed.

		private static readonly uint[] LMods = { VK_LSHIFT, VK_LCONTROL, VK_LMENU, VK_LWIN };
		private static readonly uint[] RMods = { VK_RSHIFT, VK_RCONTROL, VK_RMENU, VK_RWIN };
		internal uint CurrentModifiersLR()
		{
			// Prefer logical (non-ignored) state from the sender when the hook is active
			// and no send is in progress. During Send, the sender may temporarily drop
			// modifiers to emit shifted characters; we should report what is physically
			// held instead so that hotkey suffix checks (e.g. +h) keep seeing Shift.
			if (HasKbdHook())
			{
				if (!sendInProgress)
					return kbdMsSender.modifiersLRLogicalNonIgnored;
				// send in progress: fall through to physical snapshot
			}

			// With no hook, fall back to logical query via X11.
			if (TryQueryModifierLRState(out var logicalMods))
				return logicalMods;

			// Last resort: use physical snapshot.
			uint mods = 0;
			if (VK_LSHIFT < physicalKeyState.Length && (physicalKeyState[VK_LSHIFT] & StateDown) != 0) mods |= MOD_LSHIFT;
			if (VK_RSHIFT < physicalKeyState.Length && (physicalKeyState[VK_RSHIFT] & StateDown) != 0) mods |= MOD_RSHIFT;
			if (VK_LCONTROL < physicalKeyState.Length && (physicalKeyState[VK_LCONTROL] & StateDown) != 0) mods |= MOD_LCONTROL;
			if (VK_RCONTROL < physicalKeyState.Length && (physicalKeyState[VK_RCONTROL] & StateDown) != 0) mods |= MOD_RCONTROL;
			if (VK_LMENU < physicalKeyState.Length && (physicalKeyState[VK_LMENU] & StateDown) != 0) mods |= MOD_LALT;
			if (VK_RMENU < physicalKeyState.Length && (physicalKeyState[VK_RMENU] & StateDown) != 0) mods |= MOD_RALT;
			if (VK_LWIN < physicalKeyState.Length && (physicalKeyState[VK_LWIN] & StateDown) != 0) mods |= MOD_LWIN;
			if (VK_RWIN < physicalKeyState.Length && (physicalKeyState[VK_RWIN] & StateDown) != 0) mods |= MOD_RWIN;
			return mods;
		}

		internal bool TryQueryModifierLRState(out uint mods, byte[]? keymapBuffer = null)
		{
			mods = 0u;

			if (!IsX11Available)
				return false;

			byte[] keymap = keymapBuffer is { Length: 32 } kb ? kb : new byte[32];

			if (XDisplay.Default.XQueryKeymap(keymap) == 0)
				return false;

			for (int keycode = 8; keycode < 256; keycode++)
			{
				var byteIndex = keycode >> 3;
				var bitMask = 1 << (keycode & 7);

				if ((keymap[byteIndex] & bitMask) == 0)
					continue;

				var keysym = (ulong)XDisplay.Default.XKeycodeToKeysym(keycode, 0);
				if (keysym == 0)
					continue;

				switch (keysym)
				{
					case 0xFFE1: mods |= MOD_LSHIFT; break;     // Shift_L
					case 0xFFE2: mods |= MOD_RSHIFT; break;     // Shift_R
					case 0xFFE3: mods |= MOD_LCONTROL; break; // Control_L
					case 0xFFE4: mods |= MOD_RCONTROL; break; // Control_R
					case 0xFFE9: mods |= MOD_LALT; break;         // Alt_L
					case 0xFFEA: mods |= MOD_RALT; break;         // Alt_R
					case 0xFFEB: mods |= MOD_LWIN; break;         // Super_L
					case 0xFFEC: mods |= MOD_RWIN; break;         // Super_R
				}
			}

			return true;
		}

		private bool TryQueryKeymap(out byte[] keymap)
		{
			keymap = new byte[32];

			if (!IsX11Available)
				return false;

			return XDisplay.Default.XQueryKeymap(keymap) != 0;
		}

		private bool TryQueryKeyState(uint vk, out bool isDown)
		{
			isDown = false;
			if (!IsX11Available)
				return false;

			if (!TryQueryKeymap(out var keymap))
				return false;

			// Compute expected keysyms for this vk (primary and alternate, e.g. upper/lower).
			var expected = VkToKeysyms(vk);
			if (expected.Count == 0)
				return false;

			for (int keycode = 8; keycode < 256; keycode++)
			{
				var byteIndex = keycode >> 3;
				var bitMask = 1 << (keycode & 7);
				if ((keymap[byteIndex] & bitMask) == 0)
					continue;

				var keysym = (ulong)XDisplay.Default.XKeycodeToKeysym(keycode, 0);
				if (keysym == 0)
					continue;

				foreach (var ks in expected)
				{
					if ((ulong)ks == keysym)
					{
						isDown = true;
						return true;
					}
				}
			}

			return true; // queried successfully; isDown stays false
		}

		private static List<uint> VkToKeysyms(uint vk)
		{
			var list = new List<uint>(2);

			// Letters: add both lower and upper keysyms
			if (vk is >= (uint)'A' and <= (uint)'Z')
			{
				var upper = vk;
				var lower = vk + 32; // ASCII lowercase
				list.Add(upper);
				list.Add(lower);
				return list;
			}

			// Digits
			if (vk is >= (uint)'0' and <= (uint)'9')
			{
				list.Add(vk);
				return list;
			}

			// Common OEM/punctuation keysyms (US layout assumptions)
			switch (vk)
			{
				case VK_SPACE: list.Add((uint)' '); return list;
				case VK_OEM_MINUS: list.Add((uint)'-'); list.Add((uint)'_'); return list;
				case VK_OEM_PLUS: list.Add((uint)'='); list.Add((uint)'+'); return list;
				case VK_OEM_1: list.Add((uint)';'); list.Add((uint)':'); return list;
				case VK_OEM_2: list.Add((uint)'/'); list.Add((uint)'?'); return list;
				case VK_OEM_3: list.Add((uint)'`'); list.Add((uint)'~'); return list;
				case VK_OEM_4: list.Add((uint)'['); list.Add((uint)'{'); return list;
				case VK_OEM_5: list.Add((uint)'\\'); list.Add((uint)'|'); return list;
				case VK_OEM_6: list.Add((uint)']'); list.Add((uint)'}'); return list;
				case VK_OEM_7: list.Add((uint)'\''); list.Add((uint)'"'); return list;
				case VK_OEM_COMMA: list.Add((uint)','); list.Add((uint)'<'); return list;
				case VK_OEM_PERIOD: list.Add((uint)'.'); list.Add((uint)'>'); return list;
			}

			// Modifiers and special keys: use X11 keysyms
			switch (vk)
			{
				case VK_LSHIFT: list.Add(0xFFE1); break; // Shift_L
				case VK_RSHIFT: list.Add(0xFFE2); break; // Shift_R
				case VK_LCONTROL: list.Add(0xFFE3); break; // Control_L
				case VK_RCONTROL: list.Add(0xFFE4); break; // Control_R
				case VK_LMENU: list.Add(0xFFE9); break; // Alt_L
				case VK_RMENU: list.Add(0xFFEA); break; // Alt_R
				case VK_LWIN: list.Add(0xFFEB); break; // Super_L
				case VK_RWIN: list.Add(0xFFEC); break; // Super_R
				case VK_RETURN: list.Add(0xFF0D); break; // Return
				case VK_TAB: list.Add(0xFF09); break; // Tab
				case VK_ESCAPE: list.Add(0xFF1B); break; // Escape
				case VK_BACK: list.Add(0xFF08); break; // Backspace
				case VK_DELETE: list.Add(0xFFFF); break; // Delete
				case VK_INSERT: list.Add(0xFF63); break; // Insert
				case VK_HOME: list.Add(0xFF50); break;
				case VK_END: list.Add(0xFF57); break;
				case VK_PRIOR: list.Add(0xFF55); break; // PageUp
				case VK_NEXT: list.Add(0xFF56); break;  // PageDown
				case VK_LEFT: list.Add(0xFF51); break;
				case VK_UP: list.Add(0xFF52); break;
				case VK_RIGHT: list.Add(0xFF53); break;
				case VK_DOWN: list.Add(0xFF54); break;
			}

			return list;
		}

		private static uint VkToModMask(uint vk) => vk switch
		{
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

		// Gets the logical state of all keys as a byte array (like GetKeyboardState on Windows).
		internal bool TryGetKeyboardState(out byte[] state)
		{
			state = new byte[VK_ARRAY_COUNT];
			var success = false;

			if (HasKbdHook())
			{
				System.Buffer.BlockCopy(logicalKeyState, 0, state, 0, Math.Min(logicalKeyState.Length, state.Length));
				success = true;
			}
			else if (TryQueryKeymap(out var logicalMap) && TryQueryModifierLRState(out var mods, logicalMap))
			{
				if ((mods & MOD_LSHIFT) != 0 && VK_LSHIFT < state.Length) state[VK_LSHIFT] = StateDown;
				if ((mods & MOD_RSHIFT) != 0 && VK_RSHIFT < state.Length) state[VK_RSHIFT] = StateDown;
				if ((mods & MOD_LCONTROL) != 0 && VK_LCONTROL < state.Length) state[VK_LCONTROL] = StateDown;
				if ((mods & MOD_RCONTROL) != 0 && VK_RCONTROL < state.Length) state[VK_RCONTROL] = StateDown;
				if ((mods & MOD_LALT) != 0 && VK_LMENU < state.Length) state[VK_LMENU] = StateDown;
				if ((mods & MOD_RALT) != 0 && VK_RMENU < state.Length) state[VK_RMENU] = StateDown;
				if ((mods & MOD_LWIN) != 0 && VK_LWIN < state.Length) state[VK_LWIN] = StateDown;
				if ((mods & MOD_RWIN) != 0 && VK_RWIN < state.Length) state[VK_RWIN] = StateDown;
				success = true;
			}

			return success;
		}

		private void OnMouseWheel(object? sender, MouseWheelHookEventArgs e)
		{
			if (!mouseEnabled) return;

			lastHookEventWasKeyboard = false;
			var vk = MapWheelVk(e);
			var sc = (uint)e.Data.Rotation;
			var isInjected = MarkSimulatedIfNeeded(e, vk, KeyCode.VcUndefined, false, out ulong extraInfo);

			_ = LowLevelCommon(e, vk, sc, sc, keyUp: false, extraInfo, 0);
		}

		private void OnMouseMoved(object? sender, MouseHookEventArgs e)
		{
			if (!mouseEnabled) return;

			lastHookEventWasKeyboard = false;
			if (!e.IsEventSimulated)
			{
				var script = Script.TheScript;
				script.timeLastInputPhysical = script.timeLastInputMouse = DateTime.UtcNow;

				if (script.KeyboardData.blockMouseMove)
					e.SuppressEvent = true;
			}
		}

		private void OnMousePressed(object? sender, MouseHookEventArgs e)
		{
			if (!mouseEnabled) return;

			lastHookEventWasKeyboard = false;
			var vk = MapMouseVk(e.Data.Button);
			var sc = 0u;
			var isInjected = MarkSimulatedIfNeeded(e, vk, KeyCode.VcUndefined, false, out ulong extraInfo);

			_ = LowLevelCommon(e, vk, sc, sc, keyUp: false, extraInfo, 0);
		}

		private void OnMouseReleased(object? sender, MouseHookEventArgs e)
		{
			if (!mouseEnabled) return;

			lastHookEventWasKeyboard = false;
			var vk = MapMouseVk(e.Data.Button);
			var sc = 0u;
			var isInjected = MarkSimulatedIfNeeded(e, vk, KeyCode.VcUndefined, true, out ulong extraInfo);

			_ = LowLevelCommon(e, vk, sc, sc, keyUp: true, extraInfo, 0);
		}

		private static uint MapMouseVk(MouseButton button) => button switch
		{
			MouseButton.Button1 => VK_LBUTTON,
			MouseButton.Button2 => VK_RBUTTON,
			MouseButton.Button3 => VK_MBUTTON,
			MouseButton.Button4 => VK_XBUTTON1,
			MouseButton.Button5 => VK_XBUTTON2,
			_ => 0u
		};

		private static uint MapWheelVk(MouseWheelHookEventArgs e)
		{
			if (e.Data.Direction == MouseWheelScrollDirection.Vertical)
				return e.Data.Rotation < 0 ? VK_WHEEL_UP : VK_WHEEL_DOWN;
			return e.Data.Rotation < 0 ? VK_WHEEL_LEFT : VK_WHEEL_RIGHT;
		}

		private static readonly FieldInfo? rawEventField = typeof(HookEventArgs).GetField("<RawEvent>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);

		private static void SetRawEvent(HookEventArgs e, UioHookEvent raw)
		{
			rawEventField?.SetValue(e, raw);
		}

		internal void RegisterSyntheticEvent(KeyCode keyCode, bool keyUp, DateTime ms, long extraInfo)
		{
			if (keyCode == KeyCode.VcUndefined)
				return;

			lock (pendingSynthetic)
			{
				pendingSynthetic.Add(new SyntheticToken(keyCode, keyUp, ms, extraInfo));
				DebugLog($"[Hook] Registered synthetic [{keyCode}] {(keyUp ? "up" : "down")} at {ms.Ticks} with extraInfo={extraInfo}");
			}
		}

		private bool PeekSyntheticEvent(KeyCode keyCode, bool keyUp, DateTimeOffset ms)
		{
			if (keyCode == KeyCode.VcUndefined)
				return false;

			lock (pendingSynthetic)
			{
				int count = pendingSynthetic.Count;
				for (int i = 0; i < count; i++)
				{
					var s = pendingSynthetic[i];
					var diffMs = (ms - s.EnqueueTime).TotalMilliseconds;
					if (diffMs > SyntheticEventTimeoutMs) // Enqueued >200ms in past, remove stale event
					{
						pendingSynthetic.RemoveAt(i);
						i--; count--;
					}
					else if (diffMs < 0) // Enqueued in future compared to hook event
					{
						break;
					}
					else
					{
						if (s.KeyCode == keyCode && s.KeyUp == keyUp)
							return true;
					}
				}
			}

			return false;
		}

		private bool ConsumeSyntheticEvent(KeyCode keyCode, bool keyUp, DateTimeOffset ms, out ulong extraInfo)
		{
			extraInfo = 0;
			if (keyCode == KeyCode.VcUndefined)
				return false;

			lock (pendingSynthetic)
			{
				int count = pendingSynthetic.Count;
				for (int i = 0; i < count; i++)
				{
					var s = pendingSynthetic[i];
					var diffMs = (ms - s.EnqueueTime).TotalMilliseconds;
					if (diffMs > SyntheticEventTimeoutMs) // Enqueued >200ms in past, remove stale event
					{
						pendingSynthetic.RemoveAt(i);
						i--; count--;
					}
					else if (diffMs < 0) // Enqueued in future compared to hook event
					{
						break;
					}
					else
					{
						if (s.KeyCode == keyCode && s.KeyUp == keyUp) 
						{
							extraInfo = (ulong)s.ExtraInfo;
							pendingSynthetic.RemoveAt(i);
							DebugLog($"[Hook] Removed synthetic [{keyCode}] {(keyUp ? "up" : "down")} at {ms.Ticks} with extraInfo={s.ExtraInfo}");
							return true;
						}
					}
				}
			}

			return false;
		}

		private void SuppressHotkeyRelease(uint vk)
		{
			if (vk == 0) return;
			lock (suppressedHotkeyReleases)
			{
				suppressedHotkeyReleases.TryGetValue(vk, out var curr);
				var next = curr + 1;
				suppressedHotkeyReleases[vk] = next;
				DebugLog($"[Hook] Suppress suffix release vk={vk} count={next}");
			}
		}

		private bool ShouldSuppressSuffixRelease(HotkeyVariant variant, uint vk, uint hotkeyIdWithFlags)
		{
			if (vk == 0) return false;

			if (variant != null && (variant.noSuppress & HotkeyDefinition.AT_LEAST_ONE_VARIANT_HAS_TILDE) != 0)
				return false;

			if (HasKeyUpHotkey(vk))
				return false;

			return activeHotkeyDown && activeHotkeyVk == vk;
		}

		private bool MarkSimulatedIfNeeded(HookEventArgs e, uint vk, KeyCode keyCode, bool keyUp, out ulong extraInfo)
		{
			var mask = e.RawEvent.Mask;
			var simulated = (mask & EventMask.SimulatedEvent) != 0;
			extraInfo = 0;

			if (!simulated && (ConsumeSyntheticEvent(keyCode, keyUp, e.EventTime, out extraInfo)
				 //|| IsLogicallyDownPhysicallyUp(vk)
				 ))
			{
				var raw = e.RawEvent;
				raw.Mask |= EventMask.SimulatedEvent;
				SetRawEvent(e, raw);
				simulated = true;
			}

			if (simulated && extraInfo == 0)
        		extraInfo = (ulong)KeyboardMouseSender.KeyIgnoreAllExceptModifier;

			return simulated;
		}

		private ulong ComputeExtraInfo(bool isSimulated)
		{
			if (sendInProgress || isSimulated)
				return (ulong)KeyboardMouseSender.KeyIgnoreAllExceptModifier;

			return 0;
		}

		private bool WasKeyGrabbed(HookEventArgs e, uint vk, out bool grabbedByHotstring)
		{
			grabbedByHotstring = false;
			if (!IsX11Available)
				return false;

			lock (hkLock)
			{
				uint xcode = e.RawEvent.Keyboard.RawCode;

				// Fallback: derive keycode from vk if RawCode was unavailable.
				if (xcode == 0 && vk != 0 && TryMapToXGrab(vk, 0, out var kcFromVk, out _))
					xcode = kcFromVk;

				if (xcode == 0)
					return false;

				uint mods = CurrentXGrabMask();
				bool hotstringGrabbed = false;

				static bool ModsMatch(uint grabbed, uint actual)
				{
					// Ignore Caps/Num/Scroll variants when comparing; we grab all variants already.
					const uint lockBits = LockMask | Mod2Mask;
					return grabbed == AnyModifier || grabbed == actual || (grabbed & ~lockBits) == (actual & ~lockBits);
				}

				bool MatchList(HashSet<(uint keycode, uint mods)> list, bool markHotstring)
				{
					foreach (var (kc, m) in list)
					{
						if (kc == xcode && ModsMatch(m, mods))
						{
							if (markHotstring)
								hotstringGrabbed = true;
							return true;
						}
					}
					return false;
				}

				var grabbed = MatchList(activeGrabs, false)
					|| MatchList(activeHotstringGrabs, true)
					|| MatchList(hsActiveGrabVariants, true);

				grabbedByHotstring = hotstringGrabbed;
				if (grabbed)
				{
					DebugLog($"[Hook] WasKeyGrabbed kc={xcode} mods={mods:X} hotstring={grabbedByHotstring}");
				}

				if (grabbed)
				{
					// The hook itself can't suppress on Linux; mark the raw event so downstream logic
					// can treat it as swallowed by XGrabKey.
					var raw = e.RawEvent;
					if ((raw.Mask & EventMask.SuppressEvent) == 0)
					{
						raw.Mask |= EventMask.SuppressEvent;
						SetRawEvent(e, raw);
					}
				}


				return grabbed;
			}
		}

		private uint CurrentXGrabMask()
		{
			uint mods = 0;
			var modifiersLR = CurrentModifiersLR();

			if ((modifiersLR & (MOD_LCONTROL | MOD_RCONTROL)) != 0) mods |= ControlMask;
			if ((modifiersLR & (MOD_LSHIFT | MOD_RSHIFT)) != 0) mods |= ShiftMask;
			if ((modifiersLR & (MOD_LALT | MOD_RALT)) != 0) mods |= Mod1Mask; // Alt / AltGr on some layouts
			if ((modifiersLR & (MOD_LWIN | MOD_RWIN)) != 0) mods |= Mod4Mask;

			// Locks are grabbed in all variants; include them for exact matches when we know the state.
			var ind = RefreshIndicatorSnapshot();
			if (ind.Caps) mods |= LockMask;
			if (ind.Num) mods |= Mod2Mask;
			if (ind.Scroll) mods |= Mod5Mask;

			return mods;
		}

		private void ReplayGrabbedKey(KeyCode kc, uint vk, uint sc, bool keyUp)
		{
			if (vk == 0) return;
			kbdMsSender.SendKeyEvent(keyUp ? KeyEventTypes.KeyUp : KeyEventTypes.KeyDown, vk, sc, default, false, KeyboardMouseSender.KeyIgnore);
			if (vk < logicalKeyState.Length)
				logicalKeyState[vk] = (byte)(keyUp ? 0 : StateDown);
		}

		// Temporarily release all grabs (keyboard + passive keys) during a send, then re-apply them.
		internal GrabSnapshot BeginSendUngrab()
		{
			DebugLog($"[Hook] BeginSendUngrab grabs={activeGrabs.Count} hs={activeHotstringGrabs.Count}");
			var snap = new GrabSnapshot { Active = IsX11Available };
			if (!snap.Active)
			{
				DebugLog("[Hook] BeginSendUngrab skipped (no xDisplay/xRoot)");
				return snap;
			}

			snap.Grabs = new HashSet<(uint keycode, uint mods)>(activeGrabs);
			snap.HotstringGrabs = new HashSet<(uint keycode, uint mods)>(activeHotstringGrabs);

			try
			{
				foreach (var (kc, mods) in snap.Grabs)
					_ = XDisplay.Default.XUngrabKey(kc, mods, (nint)XDisplay.Default.Root.ID);
				foreach (var (kc, mods) in snap.HotstringGrabs)
					_ = XDisplay.Default.XUngrabKey(kc, mods, (nint)XDisplay.Default.Root.ID);

				_ = XDisplay.Default.XUngrabKeyboard(CurrentTime);
				_ = XDisplay.Default.XSync(false);
				_ = XDisplay.Default.XFlush();
			}
			catch { /* best-effort */ }

			return snap;
		}

		internal void EndSendUngrab(GrabSnapshot snap)
		{
			if (!snap.Active || !IsX11Available)
				return;

			try
			{
				foreach (var (xcode, mods) in snap.Grabs)
					_ = XDisplay.Default.XGrabKey(xcode, mods, (nint)XDisplay.Default.Root.ID, true, GrabModeAsync, GrabModeAsync);
				foreach (var (xcode, mods) in snap.HotstringGrabs)
					_ = XDisplay.Default.XGrabKey(xcode, mods, (nint)XDisplay.Default.Root.ID, true, GrabModeAsync, GrabModeAsync);

				_ = XDisplay.Default.XSync(false);
				_ = XDisplay.Default.XFlush();
			}
			catch { /* best-effort */ }
			DebugLog($"[Hook] EndSendUngrab grabs={snap.Grabs?.Count ?? 0} hs={snap.HotstringGrabs?.Count ?? 0}");
		}

		internal uint TemporarilyUngrabKey(uint vk)
		{
			if (!IsX11Available) return 0;

			uint deactivated = 0;

			// VK -> KeySym -> keycode
			ulong ks = VkToKeysym(vk);
			if (ks == 0) return 0;

			var targetXcode = XDisplay.Default.XKeysymToKeycode((IntPtr)ks);
			if (targetXcode == 0) return 0;

			try
			{
				foreach (var (xcode, mods) in activeGrabs)
				{
					if (xcode == targetXcode) 
					{
						_ = XDisplay.Default.XUngrabKey(xcode, mods, (nint)XDisplay.Default.Root.ID);
						deactivated = xcode;
					}
				}
				foreach (var (xcode, mods) in activeHotstringGrabs)
				{
					if (xcode == targetXcode)
					{
						_ = XDisplay.Default.XUngrabKey(xcode, mods, (nint)XDisplay.Default.Root.ID);
						deactivated = xcode;
					}
				}

				_ = XDisplay.Default.XUngrabKeyboard(CurrentTime);
				_ = XDisplay.Default.XSync(false);
				_ = XDisplay.Default.XFlush();
			}
			catch { /* best-effort */ }

			return deactivated;
		}

		internal void RegrabKey(uint targetXcode)
		{
			if (targetXcode == 0) return;
			try
			{
				foreach (var (xcode, mods) in activeGrabs)
				{
					if (xcode == targetXcode) 
						_ = XDisplay.Default.XGrabKey(xcode, mods, (nint)XDisplay.Default.Root.ID, true, GrabModeAsync, GrabModeAsync);
				}
				foreach (var (xcode, mods) in activeHotstringGrabs)
				{
					if (xcode == targetXcode)
						_ = XDisplay.Default.XGrabKey(xcode, mods, (nint)XDisplay.Default.Root.ID, true, GrabModeAsync, GrabModeAsync);
				}

				_ = XDisplay.Default.XUngrabKeyboard(CurrentTime);
				_ = XDisplay.Default.XSync(false);
				_ = XDisplay.Default.XFlush();
			}
			catch { /* best-effort */ }
		}

		/*
		internal void WaitForPendingSyntheticDrain(int timeoutMs)
		{
			var sw = Stopwatch.StartNew();
			while (sw.ElapsedMilliseconds < timeoutMs)
			{
				lock (pendingSynthetic)
				{
					bool any = false;
					foreach (var kv in pendingSynthetic)
					{
						if (kv.Value.Down != 0 || kv.Value.Up != 0)
						{
							any = true;
							break;
						}
					}
					if (!any)
						return;
				}
				Thread.Sleep(1);
			}
		}
		*/

		internal void DisarmHotstring()
		{
			if (!hsArmed)
				return;

			lock (hkLock)
			{
				DebugLog("Disarming hotstring");

				if (IsX11Available)
				{
					foreach (var (kc, mods) in hsActiveGrabVariants)
						_ = XDisplay.Default.XUngrabKey(kc, mods);

					hsActiveGrabVariants.Clear();
					activeHotstringGrabs.Clear();
					_ = XDisplay.Default.XUngrabKeyboard(CurrentTime);
					_ = XDisplay.Default.XSync(false);
				}

				hsArmedEnds.Clear();
				hsArmed = false;
			}
		}

		// Arm a set of end characters (all that would complete a match NOW)
		private void ArmHotstringForEnds(HashSet<char> endsToArm)
		{
			DisarmHotstring();
			if (endsToArm.Count == 0 || !IsX11Available)
				return;

			lock (hkLock)
			{
				foreach (var endChar in endsToArm)
				{
					if (!MapEndCharToVkAndNeeds(endChar, out var vk, out var needShift, out var needAltGr))
						continue;

					var ks = VkToKeysym(vk);
					if (ks == 0)
						continue;

					uint xcode = XDisplay.Default.XKeysymToKeycode((IntPtr)ks);
					if (xcode == 0)
						continue;

					uint baseMods = 0;
					if (needShift) baseMods |= ShiftMask;
					if (needAltGr) baseMods |= Mod5Mask; // AltGr

					// Grab with CapsLock/NumLock variants like elsewhere.
					// Grab with the base mods plus Caps/NumLock variants. Allow Shift as an extra
					// modifier even when it isn't required so that shifted end-keys (e.g. holding
					// Shift while pressing Space) still get swallowed and don't leak through to
					// the target app when firing uppercase hotstrings.
					var modsToGrab = new HashSet<uint>
					{
						baseMods,
						baseMods | LockMask,
						baseMods | Mod2Mask,
						baseMods | LockMask | Mod2Mask
					};

					if (!needShift)
					{
						modsToGrab.Add(baseMods | ShiftMask);
						modsToGrab.Add(baseMods | ShiftMask | LockMask);
						modsToGrab.Add(baseMods | ShiftMask | Mod2Mask);
						modsToGrab.Add(baseMods | ShiftMask | LockMask | Mod2Mask);
					}

					foreach (var mods in modsToGrab)
					{
						_ = XDisplay.Default.XGrabKey(xcode, mods, (nint)XDisplay.Default.Root.ID, true, GrabModeAsync, GrabModeAsync);
						hsActiveGrabVariants.Add((xcode, mods));
						if (!activeHotstringGrabs.Contains((xcode, mods)))
							activeHotstringGrabs.Add((xcode, mods));
					}

					hsArmedEnds.Add(new ArmedEnd
					{
						Keycode = xcode,
						XModsBase = baseMods,
						Vk = vk,
						EndChar = endChar,
						NeedShift = needShift,
						NeedAltGr = needAltGr
					});
				}

				if (hsActiveGrabVariants.Count > 0)
				{
					XDisplay.Default.XSync(false);
					hsArmed = true;
				}
			}
		}

		// After we mutate the hs buffer, call this to (re)compute arming
		private void RecomputeHotstringArming()
		{
			var script = Script.TheScript;
			var hm = script.HotstringManager;

			// 1) If a full match exists already (e.g., non-terminating HS), trigger immediately.
			var ready = hm.MatchHotstring();
			if (ready != null)
			{
				char? evtChar = ' ';
				if (!KeyboardMouseSender.HotInputLevelAllowsFiring(ready.inputLevel, lastTypedExtraInfo, ref evtChar))
				{
					LogHotstringBuffer($"match gated by inputlevel={ready.inputLevel} lastExtra={lastTypedExtraInfo}");
					DisarmHotstring();
					return;
				}

				// Determine case + end char like Windows (same code you already use below)
				var caseMode = ComputeCaseMode(ready, hm.hsBuf);

				char endChar = '\0';
				var sspan = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(hm.hsBuf);
				if (ready.endCharRequired)
				{
					if (sspan.Length > 0)
						endChar = sspan[^1];
					else
						endChar = lastTypedChar;
				}

				_ = channel.Writer.TryWrite(new KeysharpMsg
				{
					message = (uint)UserMessages.AHK_HOTSTRING,
					obj = new HotstringMsg { hs = ready, caseMode = caseMode, endChar = endChar }
				});
				LogHotstringBuffer($"hotstring fired '{ready.Name}' end='{(endChar == '\0' ? "\\0" : endChar.ToString())}'");
				ClearHotstringBuffer("fired");

				DisarmHotstring();
				return;
			}

			// 2) Predict: append each possible end-char; if MatchHotstring() would succeed, arm that end-char.
			var ends = new HashSet<char>();

			// Prefer manager’s configured defaults; fall back to a sensible superset if needed.
			var def = hm.defEndChars ?? string.Empty;
			foreach (var c in def)
			{
				hm.hsBuf.Add(c);
				var m = hm.MatchHotstring();
				hm.hsBuf.RemoveAt(hm.hsBuf.Count - 1);
				if (m != null)
					ends.Add(c);
			}

			// If manager had no defaults or nothing matched, you can optionally include extra common terminators:
			// foreach (var c in " \t\r\n.,;:!?-") { ... }  // (uncomment if you want a bit more aggressive prediction)

			// Arm (or disarm) accordingly
			if (ends.Count > 0) ArmHotstringForEnds(ends);
			else DisarmHotstring();
		}

		// "Physically" release the given key (if held) and optionally mark it so hotstring logic ignores its release.
		private void ForceReleaseKeyX11(uint vk, bool markHotstring)
		{
			if (!IsX11Available) return;
			if (vk == 0) return;

			// Send a synthetic KeyRelease now, so the target won’t think the key is held.
			if (kbdMsSender is LinuxKeyboardMouseSender sender)
				sender.SimulateKeyEvent(vk, isPress: false, KeyIgnoreAllExceptModifier);

			// Update our local state and ignore the real release we’ll get shortly
			if (vk < logicalKeyState.Length)
				logicalKeyState[vk] = 0;
		}

		internal void ForceReleaseEndKeyX11(uint vk) => ForceReleaseKeyX11(vk, markHotstring: true);

		// Force an X11 press/release for vk and mark it synthetic for the hook.
		private void ForceKeyEventX11(uint vk, bool isPress)
		{
			if (!IsX11Available) return;
			if (vk == 0) return;

			// Make sure the hook can recognize it as simulated even if uiohook doesn't set the flag.
			if (kbdMsSender is LinuxKeyboardMouseSender sender)
				sender.SimulateKeyEvent(vk, isPress, KeyIgnoreAllExceptModifier);

			// Keep our logical snapshot aligned with the "server logical" state we just forced.
			if (vk < logicalKeyState.Length)
				logicalKeyState[vk] = (byte)(isPress ? StateDown : 0);
		}

		internal readonly struct ReleasedKey
		{
			public readonly uint Vk;
			public ReleasedKey(uint vk) { Vk = vk; }
		}

		private static bool IsModifierKeysym(ulong ks) => ks is
			0xFFE1 or 0xFFE2 or // Shift_L / Shift_R
			0xFFE3 or 0xFFE4 or // Control_L / Control_R
			0xFFE9 or 0xFFEA or // Alt_L / Alt_R
			0xFFEB or 0xFFEC;   // Super_L / Super_R

		internal List<ReleasedKey> ForceReleaseKeysForSend(HashSet<uint> vks)
		{
			var released = new List<ReleasedKey>(16);
			var releasedVks = new HashSet<uint>();

			if (!IsX11Available)
				return released;

			var keymap = new byte[32];
			if (XDisplay.Default.XQueryKeymap(keymap) == 0)
				return released;

			for (uint xk = 8; xk < 256; xk++)
			{
				var byteIndex = (int)(xk >> 3);
				var bitMask = 1 << (int)(xk & 7);
				if ((keymap[byteIndex] & bitMask) == 0)
					continue; // X11 does not think this keycode is down

				// Try to classify.
				ulong ks = 0;
				uint vk = 0;
				try
				{
					ks = (ulong)XDisplay.Default.XKeycodeToKeysym((int)xk, 0);
					if (ks != 0)
						vk = VkFromKeysym(ks);
				}
				catch { /* ignore mapping failures */ }

				// Skip modifiers (either by vk or by keysym).
				if ((vk != 0 && IsModifierVk(vk)) || (ks != 0 && IsModifierKeysym(ks)))
					continue;

				// Skip mouse/wheel vks if they somehow map here.
				if (vk != 0 && (MouseUtils.IsMouseVK(vk) || MouseUtils.IsWheelVK(vk)))
					continue;
				
				if (vk == 0 || !vks.Contains(vk))
					continue; // not in the requested set

				if (!releasedVks.Add(vk))
					continue;

				// Force it up via the sender.
				ForceKeyEventX11(vk, isPress: false);
				released.Add(new ReleasedKey(vk));
			}

			return released;
		}

		/// <summary>
		/// Re-press keys that we previously force-released, but only if they are STILL physically held.
		/// This keeps our logical snapshot aligned with the user still holding the key.
		/// </summary>
		internal void RestoreNonModifierKeysAfterSend(List<ReleasedKey> released)
		{
			if (released == null || released.Count == 0)
				return;

			for (int i = 0; i < released.Count; i++)
			{
				var rk = released[i];
				if (rk.Vk == 0)
					continue;

				// Only restore if we can confirm the user still holds it.
				// (This is why we keep using the hook physical snapshot here.)
				if (rk.Vk >= physicalKeyState.Length)
					continue;

				if ((physicalKeyState[rk.Vk] & StateDown) == 0)
					continue;

				if (IsHotkeySuffixDown(rk.Vk))
					continue;

				ForceKeyEventX11(rk.Vk, isPress: true);
			}
		}

		private void UpdateLogicalKeyFromHook(uint vk, bool keyUp, bool wasGrabbed)
		{
			if (vk == 0 || vk >= logicalKeyState.Length)
				return;

			if (!wasGrabbed)
				logicalKeyState[vk] = (byte)(keyUp ? 0 : StateDown);
		}

		// Map a candidate end character to VK + required modifiers (layout-aware)
		private bool MapEndCharToVkAndNeeds(char endChar, out uint vk, out bool needShift, out bool needAltGr)
		{
			vk = 0; needShift = needAltGr = false;

			switch (endChar)
			{
				case ' ': vk = VK_SPACE; return true;
				case '\t': vk = VK_TAB; return true;
				case '\r':
				case '\n': vk = VK_RETURN; return true;
			}

			// Use the same mapper you use for Send (handles punctuation layout properly)
			if (System.Text.Rune.TryGetRuneAt(endChar.ToString(), 0, out var rune)
				&& LinuxKeyboardMouseSender.LinuxCharMapper.TryMapRuneToKeystroke(rune, out var mappedVk, out var s, out var g))
			{
				vk = mappedVk; needShift = s; needAltGr = g;
				return vk != 0;
			}
			return false;
		}


		private IndicatorSnapshot RefreshIndicatorSnapshot()
		{
			if (TryGetIndicatorStates(out var capsOn, out var numOn, out var scrollOn))
			{
				indicatorSnapshot = new IndicatorSnapshot(capsOn, numOn, scrollOn);
				return indicatorSnapshot;
			}

			return indicatorSnapshot;
		}

		// -------------------- basic matcher → AHK_HOOK_HOTKEY --------------------
		private static short PhysicalInputLevel => (short)(Keysharp.Core.Common.Keyboard.KeyboardMouseSender.SendLevelMax + 1);

		internal bool HasKeyUpHotkey(uint vk)
		{
			lock (hkLock)
			{
				foreach (var hk in linuxHotkeys)
				{
					if (hk.Vk == vk && hk.KeyUp)
						return true;
				}
			}
			return false;
		}

		internal void ResetActiveHotkeyState()
		{
			activeHotkeyDown = false;
			activeHotkeyVk = 0;
			activeHotkeyKc = KeyCode.VcUndefined;
			lock (suppressedHotkeyReleases)
				suppressedHotkeyReleases.Clear();
		}

		private bool IsLogicallyDownPhysicallyUp(uint vk)
		{
			if (vk == 0) return false;

			if (vk >= physicalKeyState.Length || (physicalKeyState[vk] & StateDown) != 0)
				return false;

			if (vk < logicalKeyState.Length && (logicalKeyState[vk] & StateDown) != 0)
				return true;

			return false;
		}

		private void PostHotkey(uint hotkeyId, uint sc /* typically 0 on Linux */, long eventLevel)
		{
			// Mirror Windows: pack SC (low word) + event input level (high word), variant omitted.
			_ = channel.Writer.TryWrite(new KeysharpMsg
			{
				message = (uint)UserMessages.AHK_HOOK_HOTKEY,
				wParam = new nint(hotkeyId),
				lParam = new nint(KeyboardUtils.MakeLong((short)sc, (short)eventLevel)),
				obj = null // <— Do NOT pass variant; UI thread recomputes it.
			});
		}

		private uint GetCurrentModifiersLRExcludingSuffix(uint suffixVk)
		{
			uint mods = 0;

			// Consider *physical* state at the time of event.
			for (uint vk = 0; vk < VK_ARRAY_COUNT; vk++)
			{
				if (vk == suffixVk) continue; // a suffix doesn't modify itself (Windows does similar) :contentReference[oaicite:17]{index=17}
				if (vk >= physicalKeyState.Length) break;
				if ((physicalKeyState[vk] & StateDown) == 0) continue;

				switch (vk)
				{
					case VK_LSHIFT: mods |= MOD_LSHIFT; break;
					case VK_RSHIFT: mods |= MOD_RSHIFT; break;
					case VK_LCONTROL: mods |= MOD_LCONTROL; break;
					case VK_RCONTROL: mods |= MOD_RCONTROL; break;
					case VK_LMENU: mods |= MOD_LALT; break;
					case VK_RMENU: mods |= MOD_RALT; break;
					case VK_LWIN: mods |= MOD_LWIN; break;
					case VK_RWIN: mods |= MOD_RWIN; break;
				}
			}
			return mods;
		}

		// -------------------- utilities & abstract impls --------------------

		/// <summary>
		/// Determine whether the given virtual key is currently logically down.
		/// </summary>
		/// <param name="vk"></param>
		internal override bool IsKeyDown(uint vk)
		{
			if (HasKbdHook())
			{
				if (vk < logicalKeyState.Length)
					return (logicalKeyState[vk] & StateDown) != 0;
			}

			// Fallback: query X11 logical state for any key.
			if (TryQueryKeyState(vk, out var isDown))
				return isDown;

			return false;
		}
		internal override bool IsKeyDownAsync(uint vk) => IsKeyDown(vk);
		internal override bool IsKeyToggledOn(uint vk)
		{
			if (TryGetIndicatorStates(out var capsOn, out var numOn, out var scrollOn))
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

		// Route hotstring collection through the Linux arming logic so end-keys can be grabbed/ungrabbed.
		internal override bool CollectHotstring(ulong extraInfo, char[] ch, int charCount, nint activeWindow,
												KeyHistoryItem keyHistoryCurr, ref HotstringDefinition hsOut, ref CaseConformModes caseConformMode, ref char endChar)
		{
			if (charCount <= 0 || ch.Length == 0)
				return true;

			if (KeyboardMouseSender.IsIgnored(extraInfo))
				return true; // Ignore simulated/send input.

			var hm = Script.TheScript.HotstringManager;
			lastTypedExtraInfo = extraInfo;

			var result = base.CollectHotstring(extraInfo, ch, charCount, activeWindow, keyHistoryCurr, ref hsOut, ref caseConformMode, ref endChar);

			// Keep Linux-side logging and arming in sync with the buffer maintained by the base collector.
			if (charCount > 0)
			{
				var c = ch[0];
				lastTypedChar = c;
				var chDesc = c switch { '\n' => "\\n", '\r' => "\\r", '\t' => "\\t", _ when char.IsControl(c) => "\\u" + ((int)c).ToString("X4"), _ => c.ToString() };

				var hmBuf = hm.hsBuf;
				var sb = new StringBuilder(hmBuf.Count * 2);
				static string Escape(char cc) => cc switch
				{
					'\n' => "\\n",
					'\r' => "\\r",
					'\t' => "\\t",
					_ when char.IsControl(cc) => "\\u" + ((int)cc).ToString("X4"),
					_ => cc.ToString()
				};
				foreach (var cc in hmBuf) sb.Append(Escape(cc));
				DebugLog($"[HS] typed '{chDesc}': len={hmBuf.Count} buf=\"{sb}\" armed={hsArmed}");

				RecomputeHotstringArming();
			}

			return result;
		}

		// Hotstrings on Linux rely on XGrabKey to swallow end characters. As soon as a hotstring
		// fires, release those grabs before the replacement is sent so the ending character can
		// be replayed by the sender.
		internal override void SendHotkeyMessages(bool keyUp, ulong extraInfo, KeyHistoryItem keyHistoryCurr, uint hotkeyIDToPost, HotkeyVariant variant, HotstringDefinition hs, CaseConformModes caseConformMode, char endChar)
		{
			if (hs != null)
				DisarmHotstring();

			if (hotkeyIDToPost != HotkeyDefinition.HOTKEY_ID_INVALID)
			{
				var vk = keyHistoryCurr.vk;
				if (vk == 0 && lastHookEventWasKeyboard)
					vk = lastKeyboardEventVk;
				if (vk != 0)
				{
					if (!keyUp && ShouldSuppressSuffixRelease(variant, vk, hotkeyIDToPost))
						SuppressHotkeyRelease(vk);
					activeHotkeyVk = vk;
					activeHotkeyDown = !keyUp;
				}
			}

			base.SendHotkeyMessages(keyUp, extraInfo, keyHistoryCurr, hotkeyIDToPost, variant, hs, caseConformMode, endChar);
		}

		internal override void PrepareToSendHotstringReplacement(char endChar)
		{
			DisarmHotstring();
		}

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
							var mapped = MapScToVk(sc);
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
					case VK_LSHIFT: return MOD_LSHIFT;
					case VK_RSHIFT: return MOD_RSHIFT;

					case VK_CONTROL:
					{
						if (sc != 0)
						{
							var mapped = MapScToVk(sc);
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
					case VK_LCONTROL: return MOD_LCONTROL;
					case VK_RCONTROL: return MOD_RCONTROL;

					case VK_MENU:
					{
						if (sc != 0)
						{
							var mapped = MapScToVk(sc);
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
					case VK_LMENU: return MOD_LALT;
					case VK_RMENU: return MOD_RALT;

					case VK_LWIN: return MOD_LWIN;
					case VK_RWIN: return MOD_RWIN;
					default:
						return 0;
				}
			}

			var mappedFromSc = MapScToVk(sc);
			return mappedFromSc switch
			{
				VK_LSHIFT => MOD_LSHIFT,
				VK_RSHIFT => MOD_RSHIFT,
				VK_LCONTROL => MOD_LCONTROL,
				VK_RCONTROL => MOD_RCONTROL,
				VK_LMENU => MOD_LALT,
				VK_RMENU => MOD_RALT,
				VK_LWIN => MOD_LWIN,
				VK_RWIN => MOD_RWIN,
				_ => 0
			};
		}

		internal override uint MapScToVk(uint sc)
		{
			if (!IsX11Available || sc == 0)
				return 0;

			var ks = (ulong)XDisplay.Default.XKeycodeToKeysym((int)sc, 0);
			return ks != 0 ? VkFromKeysym(ks) : 0;
		}

		internal override uint MapVkToSc(uint vk, bool returnSecondary = false)
		{
			if (!IsX11Available || vk == 0)
				return 0;

			ulong ks = VkToKeysym(vk);
			if (ks == 0) return 0;

			return (uint)XDisplay.Default.XKeysymToKeycode((IntPtr)ks);
		}

		internal override bool EarlyCollectInput(ulong extraInfo, uint rawSC, uint vk, uint sc, bool keyUp, bool isIgnored
										, CollectInputState state, KeyHistoryItem keyHistoryCurr)
		// Returns true if the caller should treat the key as visible (non-suppressed).
		// Always use the parameter aVK rather than event.vkCode because the caller or caller's caller
		// might have adjusted aVK, such as to make it a left/right specific modifier key rather than a
		// neutral one. On the other hand, event.scanCode is the one we need for ToUnicodeEx() calls.
		{
			var script = Script.TheScript;
			state.earlyCollected = true;
			state.used_dead_key_non_destructively = false;
			state.charCount = 0;

			if (keyUp && !CollectKeyUp(extraInfo, vk, sc, true))
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
			var activeWindow = WindowManager.GetForegroundWindowHandle(); // Set default in case there's no focused control.
			var activeWindowKeybdLayout = GetKeyboardLayout(0);
			state.activeWindow = activeWindow;
			state.keyboardLayout = activeWindowKeybdLayout;

			// SUMMARY OF DEAD KEY ISSUE:
			// Calling ToUnicodeEx() with conventional parameters disrupts the entry of dead keys in two different ways:
			//  1) Passing a dead key buffers it within the keyboard layout's internal state.
			//  2) Passing a live key removes any pending dead key from the keyboard layout's internal state.
			// In either case, the state is then incorrect for the active window's own call to ToUnicodeEx(), so it ends
			// up with something like "e" or "''e" instead of "é".  Originally this was solved by reinserting the pending
			// dead key (first by re-sending the dead key, then later by calling ToUnicodeEx()), but now we use a special
			// combination of parameters to avoid changing the state where possible.

			int charCount;
			var ch = new char[8];

			if (vk == VK_PACKET)
			{
				// VK_PACKET corresponds to a SendInput event with the KEYEVENTF_UNICODE flag.
				charCount = 1;// SendInput only supports a single 16-bit character code.
				ch[0] = (char)rawSC; // No translation needed.
			}
			else if (transcribeKey && vk != VK_MENU)
			{
				var keyState = new byte[physicalKeyState.Length];
				bool interfere = pendingDeadKeys.Count > 0 && pendingDeadKeyInvisible;

				if (interfere)
				{
					// Either an invisible InputHook is in progress or there is a UWP app focused.  In either case, the dead key
					// was only recorded by us and wasn't retained by the keyboard layout's internal state, so we need to "replay"
					// the sequence to set things up for conversion of the new key.
					for (int i = 0; i < pendingDeadKeys.Count; ++i)
					{
						var dead_key = pendingDeadKeys[i];
						AdjustKeyState(keyState, dead_key.modLR);
						keyState[VK_CAPITAL] = (byte)dead_key.caps;
						System.Array.Clear(ch, 0, ch.Length);
						_ = ToUnicode(dead_key.vk, dead_key.sc, keyState, ch, 0, activeWindowKeybdLayout);
					}
				}

				// The documentation for ToAsciiEx is incomplete, but recent documentation for ToUnicodeEx shows the meaning of
				// the flags: 0x1 = Alt+Numpad key combinations are not handled (but the flag doesn't prevent Alt-up itself from
				// being processed), 0x2 = handle key break events (key-up).  We fake key-up to avoid changing the dead key state
				// (it stands to reason that key-down normally causes a change in state, so the corresponding key-up wouldn't).
				// We must avoid passing VK_MENU with KBDBREAK (0x8000) because that disrupts any ongoing Alt+Numpad entry.
				// Note that Windows 10 v1607 supports flag 0x4 to avoid changing the keyboard state, but there seems to be no
				// benefit; in particular, the Alt+Numpad state is still affected.
				// Credit to Ilya Zakharevich for pointing out this method @ https://stackoverflow.com/a/78173420/894589
				var flags = interfere ? 1u : 3u;
				var scanCode = rawSC | (interfere ? 0u : 0x8000u);
				// Provide the correct logical modifier and CapsLock state for any translation below.
				AdjustKeyState(keyState, kbdMsSender.modifiersLRLogical);
				keyState[VK_CAPITAL] = (byte)(IsKeyToggledOn(VK_CAPITAL) ? 1 : 0);
				System.Array.Clear(ch, 0, ch.Length);
				charCount = ToUnicode(vk, scanCode, keyState, ch, flags, activeWindowKeybdLayout);

				if (charCount == 0 && (kbdMsSender.modifiersLRLogical & (MOD_LALT | MOD_RALT)) != 0 && (kbdMsSender.modifiersLRLogical & (MOD_LCONTROL | MOD_RCONTROL)) == 0u && !interfere)
				{
					// Apparently, ToUnicodeEx ignores the Alt in Alt and Alt+Shift combinations only if the key-up bit is not set.
					// For consistency with prior versions (and Win, but not Ctrl/Shift), let the Alt state be ignored under these
					// conditions.  transcribe_key and modifier state checked above imply that the M option was used.
					keyState[VK_MENU] = 0;
					System.Array.Clear(ch, 0, ch.Length);
					charCount = ToUnicode(vk, scanCode, keyState, ch, flags, activeWindowKeybdLayout);
				}

				if (charCount <= 0 && interfere) // A key with no text translation, or possibly a chained dead key (if < 0).
				{
					// Flush the dead key which was buffered either by the ToUnicodeEx call above or the dead key loop further up.
					var ignored = new char[8];

					// Michael S. Kaplan blogged that he would explain in a later post why he used VK_SPACE to clear the buffer,
					// but then changed to using VK_DECIMAL and apparently never explained either choice.  Still, VK_DECIMAL
					// seems like a safe choice for clearing the state; probably any key which produces text will work, but
					// the loop is needed in case of an unconventional layout which makes VK_DECIMAL itself a dead key.
					while (ToUnicode(VK_DECIMAL, 0, keyState, ignored, flags, activeWindowKeybdLayout) == -1) ;
				}

				if (charCount > 0)
				{
					state.used_dead_key_non_destructively = pendingDeadKeys.Count > 0 && !interfere;
					pendingDeadKeys.Clear();
					pendingDeadKeyInvisible = false;
				}
				else if (charCount < 0 && pendingDeadKeys.Count < 3)
				{
					// Record this dead key so that we can reproduce the sequence when needed.
					var deadKey = new DeadKeyRecord()
					{
						vk = vk,
						sc = rawSC,
						modLR = kbdMsSender.modifiersLRLogical,
						caps = keyState[VK_CAPITAL]
					};
					pendingDeadKeys.Add(deadKey);
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

			// If Backspace is pressed after a dead key, ch[0] is the "dead" char and ch[1] is '\b'.
			if (vk == VK_BACK && charCount > 0)
				charCount--;// Remove '\b' to simplify the backspacing and collection stages.

			state.ch = ch;
			state.charCount = charCount;

			if (!CollectInputHook(extraInfo, vk, sc, ch, charCount, true))
				return false; // Suppress.

			return true;//Visible.
		}
										
		/*
		{
			state.earlyCollected = true;
			state.used_dead_key_non_destructively = false;
			state.charCount = 0;
			state.ch = System.Array.Empty<char>();
			state.activeWindow = IntPtr.Zero;
			state.keyboardLayout = IntPtr.Zero;

			if (keyUp)
				return CollectKeyUp(extraInfo, vk, sc, true);

			// Mirror Windows logic: only transcribe when no modifiers, shift-only, or AltGr (Ctrl+Alt).
			var mods = kbdMsSender.modifiersLRLogical;
			var nonShiftMods = mods & ~(MOD_LSHIFT | MOD_RSHIFT);
			var hasAltGr = (mods & (MOD_LCONTROL | MOD_RCONTROL)) != 0 && (mods & (MOD_LALT | MOD_RALT)) != 0;
			var transcribeKey = nonShiftMods == 0 || hasAltGr;

			if (!transcribeKey)
				return true;

			if (!IsX11Available || xDisplay == IntPtr.Zero)
				return true;

			// Translate keycode + modifiers to a character using the current layout.
			int keycode = (int)rawSC;
			bool shift = (mods & (MOD_LSHIFT | MOD_RSHIFT)) != 0;
			bool altgr = hasAltGr || (mods & MOD_RALT) != 0;
			int idx = (altgr ? 2 : 0) + (shift ? 1 : 0);

			uint ks = (uint)XKeycodeToKeysym(xDisplay, keycode, idx);
			if (ks == 0 && idx != 0)
				ks = (uint)XKeycodeToKeysym(xDisplay, keycode, 0); // fallback to base

			if (ks != 0 && KeysymToChar(ks, out var ch))
			{
				state.charCount = 1;
				state.ch = new[] { ch };
			}

			return true;
		}
		*/

		internal override uint CharToVKAndModifiers(char ch, ref uint? modifiersLr, nint keybdLayout, bool enableAZFallback = false)
		{
			// Delegate to the Linux char mapper used by the sender; add Shift/AltGr if needed.
			if (Rune.TryGetRuneAt(ch.ToString(), 0, out var rune)
				&& LinuxKeyboardMouseSender.LinuxCharMapper.TryMapRuneToKeystroke(rune, out var vk, out var needShift, out var needAltGr))
			{
				uint mods = modifiersLr ?? 0;
				if (needShift) mods |= MOD_LSHIFT;
				if (needAltGr) mods |= MOD_RALT;
				modifiersLr = mods;
				return vk;
			}
			return 0;
		}

		internal override bool SystemHasAnotherKeybdHook() => false;
		internal override bool SystemHasAnotherMouseHook() => false;

		private void ClearHotstringBuffer(string reason)
		{
			var hm = Script.TheScript.HotstringManager;
			if (hm.hsBuf.Count == 0)
			{
				LogHotstringBuffer(reason);
				return;
			}

			hm.hsBuf.Clear();
			LogHotstringBuffer(reason);
		}

		private void LogHotstringBuffer(string reason)
		{
			var hm = Script.TheScript.HotstringManager;
			var sb = new StringBuilder(hm.hsBuf.Count * 2);

			static string EscapeChar(char c) => c switch
			{
				'\n' => "\\n",
				'\r' => "\\r",
				'\t' => "\\t",
				_ when char.IsControl(c) => "\\u" + ((int)c).ToString("X4"),
				_ => c.ToString()
			};

			foreach (var ch in hm.hsBuf)
				sb.Append(EscapeChar(ch));

			DebugLog($"[HS] {reason}: len={hm.hsBuf.Count} buf=\"{sb}\" armed={hsArmed}");
		}

		// -------------------- X11 (blocking) --------------------

		private bool TryMapToXGrab(uint vk, uint modifiersLR, out uint keycode, out uint mods)
		{
			keycode = 0; mods = 0;
			if (!IsX11Available) return false;

			// VK -> KeySym -> keycode
			ulong ks = VkToKeysym(vk);
			if (ks == 0) return false;

			keycode = XDisplay.Default.XKeysymToKeycode((IntPtr)ks);
			if (keycode == 0) return false;

			// LR modifiers → X11 masks
			if ((modifiersLR & (MOD_LCONTROL | MOD_RCONTROL)) != 0) mods |= ControlMask;
			if ((modifiersLR & (MOD_LSHIFT | MOD_RSHIFT)) != 0) mods |= ShiftMask;
			if ((modifiersLR & (MOD_LALT | MOD_RALT)) != 0) mods |= Mod1Mask; // Alt
			if ((modifiersLR & (MOD_LWIN | MOD_RWIN)) != 0) mods |= Mod4Mask; // Super

			return true;
		}

		private void GrabKey(uint keycode, uint modifiers)
		{
			GrabKeyVariantsInto(keycode, modifiers, activeGrabs);
		}

		private void GrabKeyVariantsInto(uint xCode, uint modifiers, HashSet<(uint keycode, uint mods)> sink, bool anyModifier = false)
		{
			if (!IsX11Available) return;

			uint[] variants = anyModifier
				? new[] { 
					AnyModifier,					
				}
				: new[]
				{
					modifiers,
					modifiers | LockMask,
					modifiers | Mod2Mask,
					modifiers | LockMask | Mod2Mask
				};

			foreach (var m in variants)
			{
				_ = XDisplay.Default.XGrabKey(xCode, m, (nint)XDisplay.Default.Root.ID, true, GrabModeAsync, GrabModeAsync);
				sink.Add((xCode, m));
			}
			XDisplay.Default.XSync(false);
		}

		private static readonly uint[] ExtraGrabMasks = { ControlMask, ShiftMask, Mod1Mask, Mod4Mask, Mod5Mask };

		private void GrabKeyWithExtraModifiers(uint xCode, uint baseMods)
		{
			if (!IsX11Available) return;

			uint[] baseVariants =
			{
				baseMods,
				baseMods | LockMask,
				baseMods | Mod2Mask,
				baseMods | LockMask | Mod2Mask
			};

			var seen = new HashSet<uint>();

			foreach (var variant in baseVariants)
			{
				// All combinations of extra modifiers (Ctrl/Alt/Super/Shift/AltGr)
				int comboCount = 1 << ExtraGrabMasks.Length;
				for (int maskBits = 0; maskBits < comboCount; maskBits++)
				{
					uint mods = variant;
					for (int i = 0; i < ExtraGrabMasks.Length; i++)
					{
						if ((maskBits & (1 << i)) != 0)
							mods |= ExtraGrabMasks[i];
					}

					if (!seen.Add(mods))
						continue;

					_ = XDisplay.Default.XGrabKey(xCode, mods, (nint)XDisplay.Default.Root.ID, true, GrabModeAsync, GrabModeAsync);
					activeGrabs.Add((xCode, mods));
				}
			}

			XDisplay.Default.XSync(false);
		}

		private void UngrabAll()
		{
			if (!IsX11Available) return;

			lock (hkLock)
			{
				foreach (var (kc, mods) in activeGrabs)
					_ = XDisplay.Default.XUngrabKey(kc, mods);
				activeGrabs.Clear();
				activeHotstringGrabs.Clear();
				XDisplay.Default.XSync(false);
			}
		}

		// -------------------- KeySym mapping for XGrabKey --------------------
		private const uint Mod5Mask = 1 << 7; // Usually ISO_Level3_Shift (AltGr) on X11

		private static ulong VkToKeysym(uint vk)
		{
			// Letters / digits: ASCII KeySym
			if (vk >= 'A' && vk <= 'Z') return vk;
			if (vk >= '0' && vk <= '9') return vk;

			return vk switch
			{
				// Modifiers
				VK_LSHIFT   => XStringToKeysym("Shift_L"),
				VK_RSHIFT   => XStringToKeysym("Shift_R"),
				VK_LCONTROL => XStringToKeysym("Control_L"),
				VK_RCONTROL => XStringToKeysym("Control_R"),
				VK_LMENU    => XStringToKeysym("Alt_L"),
				VK_RMENU    => XStringToKeysym("Alt_R"),
				VK_LWIN     => XStringToKeysym("Super_L"),
				VK_RWIN     => XStringToKeysym("Super_R"),

				// Locks
				VK_CAPITAL  => XStringToKeysym("Caps_Lock"),
				VK_NUMLOCK  => XStringToKeysym("Num_Lock"),
				VK_SCROLL   => XStringToKeysym("Scroll_Lock"),

				// Core editing / navigation
				VK_BACK     => XStringToKeysym("BackSpace"),
				VK_DELETE   => XStringToKeysym("Delete"),
				VK_INSERT   => XStringToKeysym("Insert"),
				VK_HOME     => XStringToKeysym("Home"),
				VK_END      => XStringToKeysym("End"),
				VK_PRIOR    => XStringToKeysym("Prior"), // Page_Up
				VK_NEXT     => XStringToKeysym("Next"),  // Page_Down

				// Misc common
				VK_SNAPSHOT => XStringToKeysym("Print"),
				VK_PAUSE    => XStringToKeysym("Pause"),
				VK_APPS     => XStringToKeysym("Menu"),

				// Existing core keys
				VK_RETURN => XStringToKeysym("Return"),
				VK_TAB    => XStringToKeysym("Tab"),
				VK_ESCAPE => XStringToKeysym("Escape"),
				VK_SPACE  => XStringToKeysym("space"),
				VK_LEFT   => XStringToKeysym("Left"),
				VK_RIGHT  => XStringToKeysym("Right"),
				VK_UP     => XStringToKeysym("Up"),
				VK_DOWN   => XStringToKeysym("Down"),

				// OEM punctuation (base physical keys)
				VK_OEM_MINUS  => XStringToKeysym("minus"),
				VK_OEM_PLUS   => XStringToKeysym("equal"),
				VK_OEM_4      => XStringToKeysym("bracketleft"),
				VK_OEM_6      => XStringToKeysym("bracketright"),
				VK_OEM_5      => XStringToKeysym("backslash"),
				VK_OEM_1      => XStringToKeysym("semicolon"),
				VK_OEM_7      => XStringToKeysym("apostrophe"),
				VK_OEM_COMMA  => XStringToKeysym("comma"),
				VK_OEM_PERIOD => XStringToKeysym("period"),
				VK_OEM_2      => XStringToKeysym("slash"),
				VK_OEM_3      => XStringToKeysym("grave"),

				// Function keys
				_ when (vk >= VK_F1 && vk <= VK_F24)
					=> XStringToKeysym($"F{(int)(vk - VK_F1 + 1)}"),

				// Keypad (optional but very useful)
				_ when (vk >= VK_NUMPAD0 && vk <= VK_NUMPAD9)
					=> XStringToKeysym($"KP_{(int)(vk - VK_NUMPAD0)}"),
				VK_DIVIDE   => XStringToKeysym("KP_Divide"),
				VK_MULTIPLY => XStringToKeysym("KP_Multiply"),
				VK_SUBTRACT => XStringToKeysym("KP_Subtract"),
				VK_ADD      => XStringToKeysym("KP_Add"),
				VK_DECIMAL  => XStringToKeysym("KP_Decimal"),
				VK_SEPARATOR=> XStringToKeysym("KP_Separator"),

				_ => 0
			};
		}

		private static uint VkFromKeysym(ulong ks)
		{
			// ASCII letters/digits.
			if ((ks >= 'A' && ks <= 'Z') || (ks >= 'a' && ks <= 'z'))
				return (uint)char.ToUpperInvariant((char)ks);
			if (ks >= '0' && ks <= '9')
				return (uint)ks;

			// Keypad digits are 0xFFB0..0xFFB9 (KP_0..KP_9)
			if (ks >= 0xFFB0 && ks <= 0xFFB9)
				return (uint)(VK_NUMPAD0 + (ks - 0xFFB0));

			// Function keys (F1..F35 are 0xFFBE..; but you only expose F1..F24)
			if (ks >= 0xFFBE && ks <= 0xFFD5) // XK_F1..XK_F24
				return (uint)(VK_F1 + (ks - 0xFFBE));

			return ks switch
			{
				// Modifiers
				0xFFE1 => VK_LSHIFT,   // Shift_L
				0xFFE2 => VK_RSHIFT,   // Shift_R
				0xFFE3 => VK_LCONTROL, // Control_L
				0xFFE4 => VK_RCONTROL, // Control_R
				0xFFE9 => VK_LMENU,    // Alt_L
				0xFFEA => VK_RMENU,    // Alt_R
				0xFFEB => VK_LWIN,     // Super_L
				0xFFEC => VK_RWIN,     // Super_R

				// Locks
				0xFFE5 => VK_CAPITAL,  // Caps_Lock
				0xFF7F => VK_NUMLOCK,  // Num_Lock
				0xFF14 => VK_SCROLL,   // Scroll_Lock

				// Core editing / navigation
				0xFF08 => VK_BACK,     // BackSpace
				0xFFFF => VK_DELETE,   // Delete
				0xFF63 => VK_INSERT,   // Insert
				0xFF50 => VK_HOME,     // Home
				0xFF57 => VK_END,      // End
				0xFF55 => VK_PRIOR,    // Prior (Page_Up)
				0xFF56 => VK_NEXT,     // Next (Page_Down)

				// Misc common
				0xFF61 => VK_SNAPSHOT, // Print
				0xFF13 => VK_PAUSE,    // Pause
				0xFF67 => VK_APPS,     // Menu

				// Existing core keys
				0xFF0D => VK_RETURN,   // Return
				0xFF09 => VK_TAB,      // Tab
				0xFF1B => VK_ESCAPE,   // Escape
				0x0020 => VK_SPACE,    // space
				0xFF51 => VK_LEFT,
				0xFF53 => VK_RIGHT,
				0xFF52 => VK_UP,
				0xFF54 => VK_DOWN,

				// OEM-ish punctuation (note: layout caveat still applies)
				0x002D => VK_OEM_MINUS,
				0x003D => VK_OEM_PLUS,
				0x005B => VK_OEM_4,
				0x005D => VK_OEM_6,
				0x005C => VK_OEM_5,
				0x003B => VK_OEM_1,
				0x0027 => VK_OEM_7,
				0x002C => VK_OEM_COMMA,
				0x002E => VK_OEM_PERIOD,
				0x002F => VK_OEM_2,
				0x0060 => VK_OEM_3,

				// Keypad ops
				0xFFAF => VK_DIVIDE,   // KP_Divide
				0xFFAA => VK_MULTIPLY, // KP_Multiply
				0xFFAD => VK_SUBTRACT, // KP_Subtract
				0xFFAB => VK_ADD,      // KP_Add
				0xFFAE => VK_DECIMAL,  // KP_Decimal
				0xFFAC => VK_SEPARATOR,// KP_Separator

				_ => 0
			};
		}


		const uint XkbUseCoreKbd = 0x0100;   // you already used this
		const uint XK_Num_Lock = 0xff7f;
		const uint XK_Scroll_Lock = 0xff14;

		internal bool TryGetIndicatorStates(out bool capsOn, out bool numOn, out bool scrollOn)
		{
			capsOn = numOn = scrollOn = false;
			var display = XDisplay.Default.Handle;
			if (display == IntPtr.Zero) return false;

			try
			{
				if (XkbGetState(display, XkbUseCoreKbd, out var st) != 0)
					return false;

				// Caps: test the locked 'Lock' modifier
				capsOn = (st.locked_mods & (byte)LockMask) != 0;

				// Num/Scroll: ask which modifier mask those keysyms map to, then test locked_mods
				var numMask = XkbKeysymToModifiers(display, XK_Num_Lock);
				var scrollMask = XkbKeysymToModifiers(display, XK_Scroll_Lock);

				numOn = (st.locked_mods & (byte)numMask) != 0;
				scrollOn = (st.locked_mods & (byte)scrollMask) != 0;
				return true;
			}
			finally
			{
			}
		}

	}
}
#endif
