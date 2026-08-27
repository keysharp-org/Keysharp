using Keysharp.Builtins;
using System.Text;

namespace Keysharp.Internals.Input.Keyboard
{
	/// <summary>
	/// Single entry point for all key-code conversions in Keysharp. Fixed/table conversions
	/// (evdev, Mac kVK, US-ASCII, VK/SC) resolve directly;
	/// the layout-aware conversions delegate to a per-platform stateful provider singleton.
	///
	/// Terminology:
	///   VK  = Windows virtual key code (always, on every platform).
	///   SC  = the active hook backend's low-level code (Windows scan code,
	///         inputd evdev code, or macOS kVK code).
	/// </summary>
	internal static partial class KeyCodes
	{
#if !WINDOWS
		/// <summary>Sentinel returned by the dead-key translation path when the provider cannot map the key.</summary>
		internal const int TranslateNotHandled = int.MinValue;

		private static readonly object providerLock = new();
		private static volatile IKeyCodeMapperProvider provider;

		static KeyCodes()
		{
			AppDomain.CurrentDomain.ProcessExit += (_, __) =>
			{
				IKeyCodeMapperProvider providerToDispose;
				lock (providerLock)
				{
					providerToDispose = provider;
					provider = null;
				}

				// A provider may tear down native observers. Never do that while holding the
				// singleton lock: native cleanup is allowed to call into a platform/UI loop.
				providerToDispose?.Dispose();
			};
		}

		/// <param name="layout">
		/// The layout token snapshotted once at the start of a send (see <see cref="KeybdLayoutRef.Value"/>)
		/// so every character of the send reuses it instead of re-querying the OS per character. On Linux
		/// this is the active layout group. Pass <c>null</c> to have the provider resolve the layout live.
		/// </param>
		public static bool TryMapRuneToKeystroke(Rune rune, nint? layout, out uint vk, out bool needShift, out bool needAltGr)
		{
			EnsureProvider();

			if (provider != null && provider.TryMapRuneToKeystroke(rune, layout, out vk, out needShift, out needAltGr))
				return true;

			needAltGr = false;
			return TryMapAsciiToVk(rune, out vk, out needShift);
		}

		/// <summary>
		/// Reverse of <see cref="TryMapRuneToKeystroke"/>: given a virtual key and the
		/// shift/AltGr state it would be pressed with, returns the character the active
		/// keyboard layout produces for that keystroke. Used to translate raw key events
		/// into typed characters (e.g. for hotstring collection) in a layout-aware way.
		/// </summary>
		public static bool TryMapKeystrokeToRune(uint vk, bool shift, bool altGr, out Rune rune)
		{
			EnsureProvider();
			rune = default;
			return provider != null && provider.TryMapKeystrokeToRune(vk, shift, altGr, out rune);
		}

		/// <summary>
		/// Stateful, dead-key-aware translation of a keystroke into characters. Mirrors the Windows
		/// ToUnicode return convention so the shared collection logic can treat all platforms alike:
		/// &gt;0 = chars written; 0 = no text; &lt;0 = dead key buffered for the next keystroke;
		/// <see cref="TranslateNotHandled"/> = provider can't translate (caller should fall back).
		/// </summary>
		public static int TranslateKeyWithDeadKeys(uint vk, bool shift, bool altGr, bool capsLock, Span<char> buffer)
		{
			EnsureProvider();
			return provider != null ? provider.TranslateKeyWithDeadKeys(vk, shift, altGr, capsLock, buffer) : TranslateNotHandled;
		}

		/// <summary>Clears any buffered dead-key composition state (e.g. when input collection (re)starts).</summary>
		public static void ResetDeadKeyState()
		{
			EnsureProvider();
			provider?.ResetDeadKeyState();
		}

		public static void ConfigureLayout(string rules = null, string model = null, string layout = null, string variant = null, string options = null)
		{
			EnsureProvider();
			provider?.ConfigureLayout(rules, model, layout, variant, options);
		}

		internal static nint GetCurrentKeymapHandle() => PreparedProvider()?.GetCurrentKeymapHandle() ?? nint.Zero;

		internal static string GetCurrentKeymapName() => PreparedProvider()?.GetCurrentKeymapName() ?? "";

		internal static nint ResolveKeyboardLayout(string layout) => PreparedProvider()?.ResolveKeyboardLayout(layout) ?? nint.Zero;

		private static IKeyCodeMapperProvider PreparedProvider()
		{
			EnsureProvider();
#if OSX
			// Explicit layout queries may prepare the UI-owned snapshot; tap callbacks never call this path.
			provider?.PrepareForInputHook(Script.TheScript);
#endif
			return provider;
		}

		/// <summary>
		/// The active keyboard-layout group. Snapshotted once per send by <see cref="KeybdLayoutRef"/>
		/// so the per-character OS query (e.g. XkbGetState on Linux) is avoided.
		/// </summary>
		internal static uint GetActiveLayoutGroup()
		{
			EnsureProvider();
			return provider != null ? provider.GetActiveLayoutGroup() : 0;
		}

#if LINUX
		internal static bool TryMapXKeycodeToVk(uint keycode, out uint vk)
		{
			EnsureProvider();
			vk = 0;
			return provider != null && provider.TryMapXKeycodeToVk(keycode, out vk);
		}

		internal static bool TryMapVkToXKeycode(uint vk, out uint keycode, bool returnSecondary = false)
		{
			EnsureProvider();
			keycode = 0;
			return provider != null && provider.TryMapVkToXKeycode(vk, out keycode, returnSecondary);
		}
#endif

#if OSX
		/// <summary>
		/// Preloads macOS keyboard-layout data and registers its change observer before a
		/// Quartz event tap starts delivering callbacks. Callback-time translation is deliberately
		/// snapshot-only and will use the fixed fallback map if preparation did not produce a layout.
		/// This method may synchronously enter the UI thread and therefore must not be called by a tap callback.
		/// </summary>
		internal static void PrepareForInputHook(Script owner)
		{
			EnsureProvider();
			provider?.PrepareForInputHook(owner);
		}

		internal static bool TryMapMacCodeToVk(uint keycode, out uint vk)
		{
			EnsureProvider();
			vk = 0;
			return provider != null && provider.TryMapMacKeyCodeToVk(keycode, out vk);
		}

		internal static bool TryMapVkToMacCode(uint vk, out uint keycode)
		{
			EnsureProvider();
			keycode = 0;
			return provider != null && provider.TryMapVkToMacKeyCode(vk, out keycode);
		}
#endif

		private static void EnsureProvider()
		{
			if (provider != null)
				return;

			IKeyCodeMapperProvider created = null;
			lock (providerLock)
			{
				if (provider != null)
					return;

				created = CreateProvider();
				provider = created;
			}

			// macOS is initialized explicitly by PrepareForInputHook before the event tap
			// starts. In particular, never make first-use translation synchronously enter UI.
#if !OSX
			created.StartLayoutChangeMonitoring();
#endif
		}

		private static IKeyCodeMapperProvider CreateProvider()
		{
#if LINUX
			return new LinuxXkbCharMapperProvider();
#elif OSX
			return new MacCharMapperProvider();
#else
			return new NullKeyCodeMapperProvider();
#endif
		}
#endif
	}

#if !WINDOWS
	internal interface IKeyCodeMapperProvider : IDisposable
	{
		bool TryMapRuneToKeystroke(Rune rune, nint? layout, out uint vk, out bool needShift, out bool needAltGr);

		/// <summary>
		/// The active keyboard-layout group. Providers without a notion of layout groups
		/// (or where it is irrelevant, e.g. macOS) return 0.
		/// </summary>
		uint GetActiveLayoutGroup() => 0;

		/// <summary>
		/// Reverse of <see cref="TryMapRuneToKeystroke"/>. Implementations that cannot do this
		/// (e.g. <see cref="NullKeyCodeMapperProvider"/>) should return false.
		/// </summary>
		bool TryMapKeystrokeToRune(uint vk, bool shift, bool altGr, out Rune rune)
		{
			rune = default;
			return false;
		}

		/// <summary>
		/// Stateful, dead-key-aware translation. See <see cref="KeyCodes.TranslateKeyWithDeadKeys"/>
		/// for the return convention. Providers without dead-key support leave this returning
		/// <see cref="KeyCodes.TranslateNotHandled"/> so the caller falls back to a stateless path.
		/// </summary>
		int TranslateKeyWithDeadKeys(uint vk, bool shift, bool altGr, bool capsLock, Span<char> buffer)
		{
			return KeyCodes.TranslateNotHandled;
		}

		/// <summary>Clears any buffered dead-key composition state.</summary>
		void ResetDeadKeyState() { }

		nint GetCurrentKeymapHandle();

		string GetCurrentKeymapName() => "";

		nint ResolveKeyboardLayout(string layout) => GetCurrentKeymapHandle();

#if LINUX
		bool TryMapXKeycodeToVk(uint keycode, out uint vk);
		bool TryMapVkToXKeycode(uint vk, out uint keycode, bool returnSecondary);
#endif

#if OSX
		bool TryMapMacKeyCodeToVk(uint keycode, out uint vk);
		bool TryMapVkToMacKeyCode(uint vk, out uint keycode);
#endif
		void ConfigureLayout(string rules, string model, string layout, string variant, string options);

		/// <summary>
		/// Starts listening for OS keyboard layout change notifications and invalidates
		/// any cached layout/character mappings (via <see cref="ConfigureLayout"/>) when
		/// the active layout changes. Called once when the provider is created.
		/// Implementations that have no such notification source can leave this as a no-op.
		/// </summary>
		void StartLayoutChangeMonitoring() { }

		/// <summary>
		/// Performs any initialization which must finish before a native input hook can call
		/// mapping methods. Implementations must keep ordinary mapping calls non-blocking with
		/// respect to the UI thread after this returns.
		/// </summary>
		void PrepareForInputHook(Script owner) => StartLayoutChangeMonitoring();
	}

	internal sealed class NullKeyCodeMapperProvider : IKeyCodeMapperProvider
	{
		public bool TryMapRuneToKeystroke(Rune rune, nint? layout, out uint vk, out bool needShift, out bool needAltGr)
		{
			vk = 0;
			needShift = false;
			needAltGr = false;
			return false;
		}

		public void ConfigureLayout(string rules, string model, string layout, string variant, string options)
		{
		}

		public nint GetCurrentKeymapHandle() => nint.Zero;

		public string GetCurrentKeymapName() => "";

		public nint ResolveKeyboardLayout(string layout) => nint.Zero;

#if LINUX
		public bool TryMapXKeycodeToVk(uint keycode, out uint vk)
		{
			vk = 0;
			return false;
		}

		public bool TryMapVkToXKeycode(uint vk, out uint keycode, bool returnSecondary)
		{
			keycode = 0;
			return false;
		}
#endif

#if OSX
		public bool TryMapMacKeyCodeToVk(uint keycode, out uint vk)
		{
			vk = 0;
			return false;
		}

		public bool TryMapVkToMacKeyCode(uint vk, out uint keycode)
		{
			keycode = 0;
			return false;
		}
#endif

		public void Dispose()
		{
		}
	}
#endif
}
