#if WINDOWS
using Keysharp.Builtins;

namespace Keysharp.Internals.Window.Windows
{
	/// <summary>
	/// Windows <see cref="IWindowEventBackend"/> built on <c>SetWinEventHook</c> (WINEVENT_OUTOFCONTEXT). Hooks are
	/// installed for the native event ids the requested categories need (refcounted, since a category may need
	/// several ids and two categories may share one); a single shared <c>WINEVENTPROC</c> normalizes each native
	/// event into a <see cref="WindowEventRaw"/>. Out-of-context hooks are delivered to the message queue of the
	/// thread that installed them, so install/uninstall is marshalled onto the UI thread (which runs the
	/// WinForms message loop); the proc therefore fires on the UI thread during normal message dispatch and
	/// never re-enters script code synchronously.
	/// </summary>
	internal sealed class WindowEventBackend : IWindowEventBackend
	{
		private readonly Script owner;

		// WINEVENTPROC dwFlags.
		private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
		// Object identifiers: apart from the text caret (CaretMove), we only care about whole top-level windows,
		// not child controls/cursor/menu items/etc.
		private const int OBJID_WINDOW = 0;
		private const int OBJID_CARET = -8;
		private const int CHILDID_SELF = 0;

		// Native event ids we map (see winuser.h).
		private const uint EVENT_SYSTEM_FOREGROUND     = 0x0003;
		private const uint EVENT_SYSTEM_MINIMIZESTART  = 0x0016;
		private const uint EVENT_SYSTEM_MINIMIZEEND    = 0x0017;
		private const uint EVENT_OBJECT_CREATE         = 0x8000;
		private const uint EVENT_OBJECT_DESTROY        = 0x8001;
		private const uint EVENT_OBJECT_SHOW           = 0x8002;
		private const uint EVENT_OBJECT_HIDE           = 0x8003;
		private const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
		private const uint EVENT_OBJECT_NAMECHANGE     = 0x800C;
		private const uint EVENT_OBJECT_CLOAKED        = 0x8017;

		private static readonly WindowEventMask[] AllBits =
		[
			WindowEventMask.Active, WindowEventMask.Create, WindowEventMask.Close, WindowEventMask.Move,
			WindowEventMask.Show, WindowEventMask.Minimize, WindowEventMask.Restore, WindowEventMask.TitleChange,
			WindowEventMask.CaretMove
		];

		/// <summary>One installed native hook: its HWINEVENTHOOK (0 if the install failed) and how many categories
		/// currently want the underlying event id.</summary>
		private sealed class HookEntry
		{
			internal nint handle;
			internal int refs;
		}

		// Installed hooks keyed by *native event id* rather than category, because categories can share one: Move and
		// CaretMove are both EVENT_OBJECT_LOCATIONCHANGE (told apart by idObject in the proc), and installing the same
		// id twice would deliver every location change twice — double-firing Move subscriptions the moment a CaretMove
		// hook existed. So each id is installed once and refcounted; a failed install is still refcounted so the
		// add/release pairing (and thus the other categories' hooks) stays correct. Only touched on the UI thread,
		// as is installedBits (which categories are currently installed).
		private readonly Dictionary<uint, HookEntry> hooks = new();
		private WindowEventMask installedBits = WindowEventMask.None;
		// Held for the backend lifetime so the GC cannot collect the delegate the OS calls.
		private readonly WindowsAPI.WinEventProc proc;
		private bool disposed;

		internal WindowEventBackend(Script owner)
		{
			this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
			proc = OnWinEvent;
		}

		public Action<WindowEventRaw> Sink { get; set; }

		public void Start(WindowEventMask mask) => owner.PostToUIThread(() => InstallOnUI(mask));

		public void Stop(WindowEventMask mask) => owner.PostToUIThread(() => UninstallOnUI(mask));

		public void Dispose()
		{
			disposed = true;
			//Must be synchronous, unlike Start/Stop: Script.Dispose() calls this and then closes the main
			//window, so a posted callback would still be sitting in the queue when the message loop ends,
			//leaving every SetWinEventHook handle installed and the pinned proc alive for the rest of the
			//process -- and a subsequent Script would install a second set and double-dispatch every event.
			//InvokeOnUIThread also keeps UnhookWinEvent on the thread that installed the hook, as required.
			owner.InvokeOnUIThread(() => UninstallOnUI((WindowEventMask)~0));
		}

		// ---- UI-thread hook management ------------------------------------------------------

		private void InstallOnUI(WindowEventMask mask)
		{
			if (disposed)
				return;

			foreach (var bit in AllBits)
			{
				if ((mask & bit) == 0 || (installedBits & bit) != 0)
					continue;

				foreach (var id in EventIdsFor(bit))
					AddRef(id, bit);

				installedBits |= bit;
			}
		}

		private void UninstallOnUI(WindowEventMask mask)
		{
			foreach (var bit in AllBits)
			{
				if ((mask & bit) == 0 || (installedBits & bit) == 0)
					continue;

				foreach (var id in EventIdsFor(bit))
					ReleaseRef(id);

				installedBits &= ~bit;
			}
		}

		/// <summary>Installs the hook for a native event id on first use, or just counts an extra user of an id that
		/// is already hooked (see <see cref="hooks"/>).</summary>
		private void AddRef(uint id, WindowEventMask bit)
		{
			if (hooks.TryGetValue(id, out var entry))
			{
				entry.refs++;
				return;
			}

			var handle = WindowsAPI.SetWinEventHook(id, id, 0, proc, 0, 0, WINEVENT_OUTOFCONTEXT);

			if (handle == 0)
				Diagnostics.Debug.WriteLine($"SetWinEventHook failed for {bit} (event 0x{id:X}).");

			hooks[id] = new HookEntry { handle = handle, refs = 1 };
		}

		/// <summary>Drops one user of a native event id, uninstalling the hook once the last one goes away.</summary>
		private void ReleaseRef(uint id)
		{
			if (!hooks.TryGetValue(id, out var entry) || --entry.refs > 0)
				return;

			if (entry.handle != 0)
				_ = WindowsAPI.UnhookWinEvent(entry.handle);

			_ = hooks.Remove(id);
		}

		// ---- native callback (runs on the UI thread) ---------------------------------------

		private void OnWinEvent(nint hWinEventHook, uint eventType, nint hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
		{
			var sink = Sink;

			if (hwnd == 0 || sink == null)
				return;

			if (idObject == OBJID_CARET)
			{
				OnCaretEvent(sink, eventType, hwnd, idChild, dwEventThread, dwmsEventTime);
				return;
			}

			// Ignore the remaining non-window objects: cursor, menu items, scrollbars, list items, etc. all arrive
			// with a non-window idObject or a non-self idChild.
			if (idObject != OBJID_WINDOW || idChild != CHILDID_SELF)
				return;

			var type = TypeFor(eventType);

			if (type == null)
				return;

			// Restrict to top-level windows (a window that is its own root ancestor) so child controls don't
			// generate window events — e.g. an edit/text control whose text changes raises EVENT_OBJECT_NAMECHANGE,
			// which would otherwise flood TitleChange subscribers. The window is already gone for Close (destroy)
			// or merely hidden (hide/cloak), so GetAncestor can't be used there; the manager keeps Close top-level
			// via its matching-window set (populated only from these top-level Create/Show events).
			if (type != WindowEventType.Close && WindowsAPI.GetAncestor(hwnd, gaFlags.GA_ROOT) != hwnd)
				return;

			// Spurious foreground events are sometimes reported for windows that aren't actually the foreground
			// window; drop those so Active fires only for the real foreground window.
			if (eventType == EVENT_SYSTEM_FOREGROUND && WindowsAPI.GetForegroundWindow() != hwnd)
				return;

			sink(new WindowEventRaw(type.Value, hwnd, ToMonotonicMs(dwmsEventTime)));
		}

		/// <summary>
		/// Handles an <c>OBJID_CARET</c> notification. The caret shares EVENT_OBJECT_LOCATIONCHANGE with window moves,
		/// so this runs whenever a Move subscription has that hook installed too and must bail out unless CaretMove is
		/// actually wanted. The caret belongs to the focused control — usually a child window — but the reported handle
		/// is its top-level ancestor, so the standard WinTitle criteria match and the callback gets the same kind of
		/// handle every other WinEvent hands it. The rectangle must be captured now (the caret is not queryable after
		/// the fact) and a caret that has already vanished is simply not reported.
		/// </summary>
		private void OnCaretEvent(Action<WindowEventRaw> sink, uint eventType, nint hwnd, int idChild, uint dwEventThread, uint dwmsEventTime)
		{
			if (eventType != EVENT_OBJECT_LOCATIONCHANGE || idChild != CHILDID_SELF
				|| (installedBits & WindowEventMask.CaretMove) == 0
				|| !TryGetCaretRect(dwEventThread, out var caret))
				return;

			var top = WindowsAPI.GetAncestor(hwnd, gaFlags.GA_ROOT);
			sink(new WindowEventRaw(WindowEventType.CaretMove, top != 0 ? top : hwnd, ToMonotonicMs(dwmsEventTime)) { Bounds = caret });
		}

		/// <summary>The caret rectangle of <paramref name="threadId"/> in screen coordinates, read through
		/// GUITHREADINFO — the same source <c>CaretGetPos</c> uses, so the two always agree. <c>rcCaret</c> is relative
		/// to the client area of the caret's own window, hence the conversion.</summary>
		private static bool TryGetCaretRect(uint threadId, out Rectangle rect)
		{
			rect = Rectangle.Empty;
			var info = GUITHREADINFO.Default;   // must be built this way: cbSize has to be populated

			if (!WindowsAPI.GetGUIThreadInfo(threadId, ref info) || info.hwndCaret == 0)
				return false;

			var pt = new POINT(info.rcCaret.Left, info.rcCaret.Top);

			if (!WindowsAPI.ClientToScreen(info.hwndCaret, ref pt))
				return false;

			rect = new Rectangle(pt.X, pt.Y, info.rcCaret.Right - info.rcCaret.Left, info.rcCaret.Bottom - info.rcCaret.Top);
			return true;
		}

		/// <summary>
		/// Normalizes the native 32-bit <paramref name="dwmsEventTime"/> (GetTickCount milliseconds at the moment the
		/// event occurred, which wraps every ~49.7 days) into the locked cross-platform WinEvent time contract: a
		/// 64-bit monotonic milliseconds-since-boot timestamp on the same clock as <see cref="Environment.TickCount64"/>
		/// (what the Linux/macOS backends emit). The full 64-bit value is reconstructed from the current tick count so
		/// it never wraps and stays comparable across backends. Out-of-context hooks are delivered within milliseconds
		/// of the event, so the unsigned 32-bit delta recovers the true event time even across a 32-bit wrap boundary;
		/// a timestamp that appears to be in the future (minor clock skew) is clamped to "now".
		/// </summary>
		private static long ToMonotonicMs(uint dwmsEventTime)
		{
			var now64 = Environment.TickCount64;
			var elapsed = (uint)now64 - dwmsEventTime;   // unsigned subtraction: correct across a 32-bit GetTickCount wrap
			return elapsed > int.MaxValue ? now64 : now64 - elapsed;
		}

		private static WindowEventType? TypeFor(uint eventType) => eventType switch
		{
			EVENT_SYSTEM_FOREGROUND     => WindowEventType.Active,
			EVENT_OBJECT_CREATE         => WindowEventType.Create,
			EVENT_OBJECT_DESTROY        => WindowEventType.Close,
			EVENT_OBJECT_HIDE           => WindowEventType.Close,   // hidden window = "closed" for a DetectHiddenWindows-off hook
			EVENT_OBJECT_CLOAKED        => WindowEventType.Close,   // cloaked (e.g. virtual desktop) = hidden
			EVENT_OBJECT_LOCATIONCHANGE => WindowEventType.Move,
			EVENT_OBJECT_SHOW           => WindowEventType.Show,
			EVENT_SYSTEM_MINIMIZESTART  => WindowEventType.Minimize,
			EVENT_SYSTEM_MINIMIZEEND    => WindowEventType.Restore,
			EVENT_OBJECT_NAMECHANGE     => WindowEventType.TitleChange,
			_ => null
		};

		private static uint[] EventIdsFor(WindowEventMask bit) => bit switch
		{
			WindowEventMask.Active      => [EVENT_SYSTEM_FOREGROUND],
			WindowEventMask.Create      => [EVENT_OBJECT_CREATE],
			WindowEventMask.Close       => [EVENT_OBJECT_DESTROY, EVENT_OBJECT_HIDE, EVENT_OBJECT_CLOAKED],
			WindowEventMask.Move        => [EVENT_OBJECT_LOCATIONCHANGE],
			WindowEventMask.Show        => [EVENT_OBJECT_SHOW],
			WindowEventMask.Minimize    => [EVENT_SYSTEM_MINIMIZESTART],
			WindowEventMask.Restore     => [EVENT_SYSTEM_MINIMIZEEND],
			WindowEventMask.TitleChange => [EVENT_OBJECT_NAMECHANGE],
			// Same native event as Move; OBJID_CARET vs OBJID_WINDOW separates them in the proc, and the id is
			// refcounted so the two categories share a single hook.
			WindowEventMask.CaretMove   => [EVENT_OBJECT_LOCATIONCHANGE],
			_ => []
		};
	}
}
#endif
