using Keysharp.Internals.Window;

namespace Keysharp.Builtins
{
	public partial class Ks
	{
		/// <summary>
		/// Cross-platform window-event subscriptions, modeled on the AHK <c>WinEvent</c> library.
		/// Each factory (<see cref="staticActive"/>, <see cref="staticExist"/>, <see cref="staticMove"/>, …)
		/// registers a callback that fires when a matching window event occurs and returns a subscription object
		/// whose <see cref="Stop"/> method cancels it. Every callback has the same shape:
		/// <c>(hook, hwnd, dwmsEventTime)</c>. Event-specific extras are exposed via <c>A_EventInfo</c> — for
		/// <c>Move</c> that's an object with <c>{ x, y, w, h }</c> (the window's position and size, matching
		/// <c>WinGetPos</c>), resolved lazily on first read, and for <c>CaretMove</c> the same shape holding the
		/// caret's screen rectangle.
		/// <para>
		/// Appearance/disappearance are reported by the DetectHiddenWindows-aware <see cref="staticExist"/> /
		/// <see cref="staticNotExist"/> pair: there is no separate Create/Close event because they were just Exist /
		/// NotExist with DetectHiddenWindows on (Exist also catches a window that starts matching via a title change,
		/// and NotExist a window that is hidden when DetectHiddenWindows is off).</para>
		/// <para>
		/// Use as: <c>#import "Ks" { WinEvent }</c> then <c>hook := WinEvent.Active(MyCallback, "ahk_exe notepad.exe")</c>.
		/// The argument order mirrors the reference library: <c>(Callback, WinTitle, Count, WinText, ExcludeTitle,
		/// ExcludeText)</c> — <c>Count</c> (default -1 = unlimited) comes right after <c>WinTitle</c>, with the
		/// rarely-used text/exclude criteria last. The subscription auto-stops on <c>__Delete</c>, but because GC
		/// timing is unpredictable, also call <c>hook.Stop()</c> (or let the owning thread tear down) when done.
		/// </para>
		/// </summary>
		public sealed class WinEvent : KeysharpObject
		{
			internal WinEventRegistration reg;

			internal WinEvent() : base() { }

			// ---- event factories (Callback, WinTitle, Count, WinText, ExcludeTitle, ExcludeText) -------------

			/// <summary>Fires when a window becomes the active/foreground window. Script: <c>WinEvent.Active(cb, …)</c>.</summary>
			[Static] public static object Active(object @this, object callback, object winTitle = null, object count = null, object winText = null, object excludeTitle = null, object excludeText = null)
				=> Subscribe(WindowEventType.Active, callback, winTitle, winText, excludeTitle, excludeText, count);

			/// <summary>Fires when a window matching the criteria appears — created, shown, or its title changed so it
			/// now matches. Fires once per matching window. Respects DetectHiddenWindows. Subsumes the old "Create"
			/// event (which was just Exist with DetectHiddenWindows on). Script: <c>WinEvent.Exist(cb, …)</c>.</summary>
			[Static] public static object Exist(object @this, object callback, object winTitle = null, object count = null, object winText = null, object excludeTitle = null, object excludeText = null)
				=> Subscribe(WindowEventType.Exist, callback, winTitle, winText, excludeTitle, excludeText, count);

			/// <summary>Fires when a window matching the criteria disappears — destroyed, hidden/cloaked, or its title
			/// changed so it no longer matches. DetectHiddenWindows-aware and subsumes the old "Close" event: with
			/// DetectHiddenWindows off a hidden window counts as gone (fires), with it on only destruction does.
			/// Script: <c>WinEvent.NotExist(cb, …)</c>.</summary>
			[Static] public static object NotExist(object @this, object callback, object winTitle = null, object count = null, object winText = null, object excludeTitle = null, object excludeText = null)
				=> Subscribe(WindowEventType.NotExist, callback, winTitle, winText, excludeTitle, excludeText, count);

			/// <summary>Fires when a window moves or resizes. Every move event is delivered as-is (not coalesced).
			/// <c>A_EventInfo</c> holds the window's new position and size as an object <c>{ x, y, w, h }</c> (matching
			/// <c>WinGetPos</c>), built on first access.</summary>
			[Static] public static object Move(object @this, object callback, object winTitle = null, object count = null, object winText = null, object excludeTitle = null, object excludeText = null)
				=> Subscribe(WindowEventType.Move, callback, winTitle, winText, excludeTitle, excludeText, count);

			/// <summary>Fires when a window is minimized.</summary>
			[Static] public static object Minimize(object @this, object callback, object winTitle = null, object count = null, object winText = null, object excludeTitle = null, object excludeText = null)
				=> Subscribe(WindowEventType.Minimize, callback, winTitle, winText, excludeTitle, excludeText, count);

			/// <summary>Fires when a window is restored from the minimized state.</summary>
			[Static] public static object Restore(object @this, object callback, object winTitle = null, object count = null, object winText = null, object excludeTitle = null, object excludeText = null)
				=> Subscribe(WindowEventType.Restore, callback, winTitle, winText, excludeTitle, excludeText, count);

			/// <summary>Fires when a window's title changes.</summary>
			[Static] public static object TitleChange(object @this, object callback, object winTitle = null, object count = null, object winText = null, object excludeTitle = null, object excludeText = null)
				=> Subscribe(WindowEventType.TitleChange, callback, winTitle, winText, excludeTitle, excludeText, count);

			/// <summary>Fires when the text caret (insertion point) moves inside a window — typing, clicking into text,
			/// arrow keys, scrolling a text view, or focus moving to another text field. <c>hwnd</c> is the caret
			/// owner's <em>top-level</em> window (not the focused edit control), so the usual WinTitle criteria apply,
			/// and <c>A_EventInfo</c> holds the caret's rectangle as <c>{ x, y, w, h }</c> in <em>screen</em>
			/// coordinates — regardless of <c>CoordMode "Caret"</c>, which only affects <c>CaretGetPos</c>.
			/// Consecutive events that report an unchanged rectangle are suppressed, and an event whose caret position
			/// can't be resolved is dropped, so the callback only ever sees a real move to a known position.
			/// <para>
			/// This rides on the same accessibility plumbing as <c>CaretGetPos</c> and inherits its coverage: Windows
			/// uses the MSAA caret (<c>EVENT_OBJECT_LOCATIONCHANGE</c> on <c>OBJID_CARET</c>), Linux the AT-SPI
			/// <c>object:text-caret-moved</c> signal, macOS the Accessibility <c>AXSelectedTextChanged</c>
			/// notification. Applications that draw their own caret without exposing it to accessibility (many
			/// browser-based/Electron editors, some game and terminal UIs) report nothing on any platform, and the
			/// Linux/macOS sources additionally need AT-SPI enabled / Accessibility permission granted.</para></summary>
			[Static] public static object CaretMove(object @this, object callback, object winTitle = null, object count = null, object winText = null, object excludeTitle = null, object excludeText = null)
				=> Subscribe(WindowEventType.CaretMove, callback, winTitle, winText, excludeTitle, excludeText, count);

			// ---- global pause -------------------------------------------------------------------------------

			/// <summary>Pauses (1), unpauses (0) or toggles (-1) all event hooks. Returns the resulting paused state.</summary>
			public static object staticPause(object @this, object newState = null)
				=> Script.TheScript.WinEventManager.SetGlobalPause(newState.Al(1L));

			/// <summary>Gets whether all event hooks are paused (script: <c>WinEvent.Paused</c>).</summary>
			public static object staticget_Paused(object @this) => Script.TheScript.WinEventManager.GlobalPaused;

			/// <summary>Sets whether all event hooks are paused (script: <c>WinEvent.Paused := …</c>).</summary>
			public static object staticset_Paused(object @this, object value)
				=> Script.TheScript.WinEventManager.SetGlobalPause(value.Ab() ? 1L : 0L);

			// ---- instance surface ----------------------------------------------------------------------------

			/// <summary>The event type this subscription listens for (e.g. "Active", "Move").</summary>
			public string EventType => reg?.type.ToString() ?? "";

			/// <summary>True while the subscription is still receiving events.</summary>
			public bool IsActive => reg?.active ?? false;

			/// <summary>Remaining number of times the callback will fire (-1 = unlimited).</summary>
			public long Count => reg?.Remaining ?? 0L;

			/// <summary>Gets or sets whether this hook is paused (paused hooks stay registered but don't fire).</summary>
			// Historical: a script's `true` arrives as an Integer, and a bool-typed setter used to fail the dynamic
			// invoke with an InvalidCastException that no script try/catch could intercept. ArgCoercer lifted that
			// constraint, so a bool-typed member would be safe now; this stays object-typed only to keep the
			// property accepting the same range of values it always has.
			public object Paused
			{
				get => reg?.paused ?? false;
				set { if (reg != null) reg.paused = value.Ab(); }
			}

			/// <summary>Pauses (1), unpauses (0) or toggles (-1) this hook. Returns the resulting paused state.</summary>
			public object Pause(object newState = null)
			{
				var r = reg;

				if (r == null)
					return false;

				var ns = newState.Al(1L);
				r.paused = ns == -1 ? !r.paused : ns != 0;
				return r.paused;
			}

			/// <summary>Cancels the subscription so the callback no longer fires.</summary>
			public object Stop()
			{
				var r = reg;

				if (r != null && r.active)
					Script.TheScript.WinEventManager.Unregister(r);

				return DefaultObject;
			}

			public override object __Delete()
			{
				_ = Stop();
				return base.__Delete();
			}

			// ---- helpers -------------------------------------------------------------------------------------

			private static object Subscribe(WindowEventType type, object callback, object winTitle, object winText, object excludeTitle, object excludeText, object count)
			{
				var fo = Functions.GetKeysharpFunc(callback, null, null, true);

				if (fo == null)
					return Errors.TypeErrorOccurred(callback, typeof(KeysharpFunc));

				var criteria = BuildCriteria(winTitle, winText, excludeTitle, excludeText);
				var remaining = count.Al(-1L);
				var scheduler = Script.TheScript.EventScheduler;
				var reg = new WinEventRegistration(type, criteria, fo, remaining, scheduler);
				var we = new WinEvent { reg = reg };
				reg.scriptObject = we;
				Script.TheScript.WinEventManager.Register(reg);
				return we;
			}

			/// <summary>Returns null (match-any) when no window filter is supplied, otherwise parses the standard AHK WinTitle criteria.</summary>
			private static SearchCriteria BuildCriteria(object winTitle, object winText, object excludeTitle, object excludeText)
			{
				if (string.IsNullOrEmpty(winTitle.As()) && string.IsNullOrEmpty(winText.As())
					&& string.IsNullOrEmpty(excludeTitle.As()) && string.IsNullOrEmpty(excludeText.As()))
					return null;

				return SearchCriteria.FromString(winTitle, winText, excludeTitle, excludeText);
			}
		}
	}
}
