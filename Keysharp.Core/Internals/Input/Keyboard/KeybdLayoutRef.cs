namespace Keysharp.Internals.Input.Keyboard
{
	/// <summary>
	/// Lazily-resolved keyboard-layout token threaded through the Send call chain.
	/// Replaces the old bare <c>nint keybdLayout</c> parameter so that a send which never
	/// maps a printable character (e.g. <c>Send "{Enter}"</c>) performs no layout work at all —
	/// not even the focused-control-thread lookup.
	///
	/// Nothing is resolved until <see cref="Value"/> is first read; the result is then cached for the
	/// rest of the send. A <c>null</c> reference stands for the script thread's layout, read at the time of use,
	/// which AutoHotkey uses for key names outside Send and InputHook, such as GetKeyState's.
	///
	/// <see cref="Value"/> is the platform's layout token, resolved lazily and meaning different things
	/// per platform: the Windows HKL; the active xkb layout <em>group</em> on Linux (the only per-send
	/// value the mapper needs — the keymap itself is provider-cached); or the current keyboard-layout
	/// data pointer on macOS.
	///
	/// Not thread-safe by design (a send runs on a single thread): this is a hand-rolled
	/// <see cref="System.Threading.LazyThreadSafetyMode.None"/> equivalent, with no factory closure allocation.
	/// </summary>
	internal sealed class KeybdLayoutRef
	{
#if WINDOWS
		private readonly uint fixedThread;   // Layout thread for ControlSend (captured during attach); used only when hasFixedThread.
		private readonly bool hasFixedThread;
#endif
		private nint value;
		private bool valueResolved;

		/// <summary>
		/// Direct-Send carrier: resolves the current foreground focused-control thread's layout on first
		/// use. Even the thread lookup is deferred, so a send of pure named keys touches nothing.
		/// </summary>
		internal KeybdLayoutRef() { }

		/// <summary>
		/// ControlSend / explicit-thread carrier: on Windows the layout thread is already known (captured
		/// while attaching to the target window), but the layout query itself is still deferred to first
		/// use. A thread id of 0 resolves to our own thread's layout (the safest default). On non-Windows
		/// there is no per-window layout thread, so this behaves like the direct-send carrier.
		/// </summary>
		internal KeybdLayoutRef(uint fixedThread)
		{
#if WINDOWS
			this.fixedThread = fixedThread;
			hasFixedThread = true;
#endif
		}

		private KeybdLayoutRef(nint resolved)
		{
			value = resolved;
			valueResolved = true;
		}

		/// <summary>
		/// Wraps an already-resolved layout handle. A handle of 0 returns <c>null</c>, the script thread's layout.
		/// </summary>
		internal static KeybdLayoutRef FromHandle(nint h) => h == 0 ? null : new KeybdLayoutRef(h);

		/// <summary>
		/// The platform's layout token (see the type remarks), queried once on first access and cached.
		/// </summary>
		internal nint Value
		{
			get
			{
				if (!valueResolved)
				{
#if WINDOWS
					// The HKL, dereferenced by VkKeyScanEx / LayoutHasAltGr.
					value = hasFixedThread ? Platform.Keys.GetKeyboardLayout(fixedThread) : Platform.Keys.GetKeyboardLayout();
#elif LINUX
					// Snapshot the active xkb group once per Send; the mapper applies the pushed desktop state.
					value = (nint)KeyCodes.GetActiveLayoutGroup();
#else
					// macOS: the current keyboard-layout data pointer (provided for symmetry; the mapper
					// caches it internally).
					value = Platform.Keys.GetKeyboardLayout();
#endif
					valueResolved = true;
				}

				return value;
			}
		}
	}
}
