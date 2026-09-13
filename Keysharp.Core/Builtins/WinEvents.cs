using Keysharp.Internals.Window;

namespace Keysharp.Builtins
{
	public partial class Ks
	{
		/// <summary>
		/// Cross-platform window events, modeled on the AHK <c>WinEvent</c> library. Construct one with the window
		/// criteria, assign a callback to each event slot it should report, then call <see cref="Start"/>:
		/// <code>
		/// we := WinEvent("ahk_exe notepad.exe")
		/// we.OnExist := (hook, hwnd, time) => TrayTip("Notepad " hwnd)
		/// we.Start()
		/// </code>
		/// <para>
		/// Every slot calls back as <c>(Hook, Hwnd, Time)</c>, and the window becomes the Last Found Window.
		/// <c>OnMove</c> and <c>OnCaretMove</c> also put a rectangle <c>{X, Y, Width, Height}</c> in
		/// <c>A_EventInfo</c>: the window's position and size, or the caret's rectangle in screen coordinates.
		/// </para>
		/// <para>
		/// The criteria are parsed, and DetectHiddenWindows, DetectHiddenText and the title-match mode captured, when
		/// the object is constructed. <c>Start()</c> seeds the matching windows and the active window silently, so
		/// every slot reports transitions after it. A running hook keeps the script running, as the library's hooks
		/// do. A new callback for a slot that has one takes effect in the running hook; setting or clearing a slot
		/// restarts a running hook on the same thread, which discards anything the old run had queued.
		/// </para>
		/// </summary>
		public sealed class WinEvent : EventHook
		{
			private readonly object[] slots = new object[WinEventManager.typeCount];
			private object winTitle = "", winText = "", excludeTitle = "", excludeText = "";
			private SearchCriteria criteria;                      // null => match any window
			private WindowSearchOptions options;
			private readonly Lock slotGate = new();               // orders slot assignments with Start's copy of them

			public WinEvent(params object[] args) : base(args) { }

			/// <summary>
			/// Parses the criteria, raising where WinExist would, and captures the calling thread's window-search
			/// settings. All four blank matches any top-level window, respecting the captured DetectHiddenWindows.
			/// </summary>
			public object __New(object winTitle = null, object winText = null, object excludeTitle = null, object excludeText = null)
			{
				this.winTitle = winTitle ?? "";
				this.winText = winText ?? "";
				this.excludeTitle = excludeTitle ?? "";
				this.excludeText = excludeText ?? "";

				static bool Blank(object value) => value is string { Length: 0 };

				if (!Blank(this.winTitle) || !Blank(this.winText) || !Blank(this.excludeTitle) || !Blank(this.excludeText))
					criteria = SearchCriteria.FromString(this.winTitle, this.winText, this.excludeTitle, this.excludeText);

				options = WinEventRegistration.CaptureSearchOptions(Script.TheScript);
				return DefaultObject;
			}

			// ---- description --------------------------------------------------------------------------------

			public object WinTitle => winTitle;
			public object WinText => winText;
			public object ExcludeTitle => excludeTitle;
			public object ExcludeText => excludeText;

			// ---- slots --------------------------------------------------------------------------------------

			/// <summary>A matching window became the foreground window, or (criteria only) the foreground window was
			/// retitled into a match.</summary>
			public object OnActive { get => Slot(WindowEventType.Active); set => SetSlot(WindowEventType.Active, value); }

			/// <summary>The foreground window stopped being a matching one: another window activated, or (criteria
			/// only) it was retitled out of the match. Hwnd is the matching window that was active last, which may be
			/// the one already active at Start().</summary>
			public object OnNotActive { get => Slot(WindowEventType.NotActive); set => SetSlot(WindowEventType.NotActive, value); }

			/// <summary>A window entered the matching windows: created, shown, restored or retitled into a match.</summary>
			public object OnExist { get => Slot(WindowEventType.Exist); set => SetSlot(WindowEventType.Exist, value); }

			/// <summary>A window left the matching windows: destroyed; hidden or cloaked while DetectHiddenWindows is
			/// off; or retitled out of the match. Hwnd may already be destroyed.</summary>
			public object OnNotExist { get => Slot(WindowEventType.NotExist); set => SetSlot(WindowEventType.NotExist, value); }

			/// <summary>A matching window moved or resized; A_EventInfo holds its rectangle at event time.</summary>
			public object OnMove { get => Slot(WindowEventType.Move); set => SetSlot(WindowEventType.Move, value); }

			/// <summary>A matching window was minimized.</summary>
			public object OnMinimize { get => Slot(WindowEventType.Minimize); set => SetSlot(WindowEventType.Minimize, value); }

			/// <summary>A matching window was restored from the minimized state.</summary>
			public object OnRestore { get => Slot(WindowEventType.Restore); set => SetSlot(WindowEventType.Restore, value); }

			/// <summary>A matching window's title changed.</summary>
			public object OnTitleChange { get => Slot(WindowEventType.TitleChange); set => SetSlot(WindowEventType.TitleChange, value); }

			/// <summary>
			/// The text caret moved inside a matching window. Hwnd is the caret owner's top-level window, and
			/// A_EventInfo holds the caret's rectangle in screen coordinates, whatever <c>CoordMode "Caret"</c> says.
			/// An unchanged position is not reported. This rides on the same accessibility plumbing as
			/// <c>CaretGetPos</c>, so an application that draws its own caret without exposing it reports nothing.
			/// </summary>
			public object OnCaretMove { get => Slot(WindowEventType.CaretMove); set => SetSlot(WindowEventType.CaretMove, value); }

			// ---- lifecycle ----------------------------------------------------------------------------------

			/// <summary>Every running WinEvent from every thread, in start order (script: <c>WinEvent.Hooks</c>). A
			/// snapshot.</summary>
			public static object staticget_Hooks(object @this) => Script.TheScript.WinEventManager.Hooks();

			/// <summary>Begins a run with the current slots; does nothing while no slot is set, since such a run could never
			/// report anything.</summary>
			public override object Start()
			{
				if (InProgress || NoSlot)
					return DefaultObject;

				EnsureMonitoring();
				EventSubscriptionBase run;

				// The gate orders only the copy of the slots with their assignments. The permission check, which may
				// prompt, and the registration, whose seeding may wait on the UI thread, run outside it.
				lock (slotGate)
					run = SwapInRun();

				run?.Register();
				return DefaultObject;
			}

			// A run takes a copy of the slots, so it fixes which events it watches.
			private protected override EventSubscriptionBase NewRun(ScriptEventScheduler owner)
				=> NoSlot ? null : new WinEventRegistration(criteria, options, (object[])slots.Clone(), owner, Script.TheScript.WinEventManager);

			private bool NoSlot => System.Array.TrueForAll(slots, callback => callback == null);

			private static void EnsureMonitoring() => _ = Script.TheScript.Permissions.EnsureWindowMonitoring(operation: "WinEvent.Start");

			private object Slot(WindowEventType slot) => slots[(int)slot] ?? DefaultObject;

			// A running hook takes a new callback for a slot that already had one in place; setting or clearing a slot
			// changes the events it watches, so that restarts it.
			private void SetSlot(WindowEventType slot, object value)
			{
				if (!TrySlotCallback(value, 3, out var callback))
					return;

				var i = (int)slot;

				// Watching another event is checked against the permission again, before the slot changes, so a refusal
				// leaves it as it was; that may prompt, so it runs outside the gate.
				if (callback != null && sub is WinEventRegistration { IsRunning: true } peek && peek.callbacks[i] == null)
					EnsureMonitoring();

				EventSubscriptionBase restarted;

				lock (slotGate)
				{
					slots[i] = callback;

					// Decided by the running run's own slots, which are what its native events and tracking were built for.
					if (sub is not WinEventRegistration { IsRunning: true } run)
						return;

					if ((run.callbacks[i] == null) == (callback == null))
					{
						run.ReplaceCallback(i, callback);
						return;
					}

					// Clearing the last slot leaves nothing to report, so the hook stops.
					if (NoSlot)
					{
						_ = Stop();
						return;
					}

					restarted = Restart();
				}

				restarted?.Register();
			}
		}
	}
}
