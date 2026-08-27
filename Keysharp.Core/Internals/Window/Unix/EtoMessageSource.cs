using Keysharp.Builtins;
#if !WINDOWS
using Keysharp.Internals.Os.Windows;
using static Keysharp.Internals.Input.Keyboard.VirtualKeys;

namespace Keysharp.Internals.Window.Unix
{
	/// <summary>
	/// Synthesizes the input window messages (mouse, keyboard, wheel) from the Eto events raised by the
	/// script's own GUI, and hands them to the OnMessage monitors through <see cref="MessageFilter"/> —
	/// the same sink <c>KeysharpForm.WndProc</c> feeds on Windows.
	/// <para>
	/// The events have to come from Eto rather than from a native event filter, because the hwnd a monitor
	/// receives is a CONTROL handle. Off Windows those are GtkWidget/NSView pointers (what <c>Gui.controls</c>
	/// is keyed on), while a native event carries a window id from an unrelated namespace, and most GTK
	/// widgets have no window of their own to be identified by. An Eto event names its control directly, is
	/// the only source that exists on Wayland and macOS, and arrives after input-method composition, so
	/// WM_CHAR carries the composed text rather than the keystrokes that produced it.
	/// </para>
	/// </summary>
	internal static class EtoMessageSource
	{
		//Whether the control is hooked, and whether its motion event is, kept in the control's own property
		//store so no separate table has to be held in step with control lifetime.
		private const string hookedKey = "KeysharpMessageHooked";
		private const string motionHookedKey = "KeysharpMessageMotionHooked";

		/// <summary>
		/// Wires a freshly created form or control. Idempotent, so it is safe to call from every path that
		/// can produce one.
		/// </summary>
		internal static void Attach(Forms.Control control)
		{
			if (control == null || control.Properties.ContainsKey(hookedKey))
				return;

			control.Properties[hookedKey] = true;
			control.MouseDown += OnMouseDown;
			control.MouseUp += OnMouseUp;
			control.MouseDoubleClick += OnMouseDoubleClick;
			control.MouseWheel += OnMouseWheel;
			control.KeyDown += OnKeyDown;
			control.KeyUp += OnKeyUp;
			control.TextInput += OnTextInput;
			SyncMotion(control);
		}

		/// <summary>
		/// Subscribing to MouseMove makes Eto add PointerMotionMask to the widget, so every pointer move over
		/// the GUI raises an event whether or not anything is listening. That one is therefore wired only while
		/// a WM_MOUSEMOVE monitor exists, and the OnMessage() functions call back here whenever that changes,
		/// since scripts register monitors both before and after building their windows.
		/// <para>
		/// The button and key subscriptions are unconditional: their masks produce an event only when the user
		/// actually presses something, whereas pointer motion is a continuous stream worth not starting.
		/// </para>
		/// </summary>
		internal static void SyncMotionHooks(Script script)
			=> script.InvokeOnUIThread(() => SyncMotionHooksCore(script));

		private static void SyncMotionHooksCore(Script script)
		{
			if (script == null || script.IsDisposed)
				return;

			foreach (var kv in script.GuiData.allGuiHwnds)
			{
				var form = kv.Value?.form;

				if (form == null)
					continue;

				SyncMotion(form);

				//Children is already the whole subtree.
				foreach (var control in form.GetControls())
					SyncMotion(control);
			}
		}

		private static void SyncMotion(Forms.Control control)
		{
			if (MotionWanted(control))
				AttachMotion(control);
			else
				DetachMotion(control);
		}

		/// <summary>
		/// Whether pointer motion has to be reported for this control: when a global or GUI-level monitor
		/// covers WM_MOUSEMOVE, or when the control itself has one. A GuiCtrl.OnMessage() monitor needs no
		/// other control hooked, because the toolkit raises the event on the one under the pointer first.
		/// <para>
		/// Asked live rather than cached, since a script may register its monitor before building the GUI
		/// the monitor is meant to cover.
		/// </para>
		/// </summary>
		private static bool MotionWanted(Forms.Control control)
			=> OwningScript(control)?.GuiData.onMessageHandlers.ContainsKey(WindowsAPI.WM_MOUSEMOVE) == true
			   || OwningGui(control)?.HasWindowMessageHandler(WindowsAPI.WM_MOUSEMOVE) == true
			   || control.GetGuiControl()?.HasWindowMessageHandler(WindowsAPI.WM_MOUSEMOVE) == true;

		private static void AttachMotion(Forms.Control control)
		{
			//Only a control the attach path above hooked: the subtree also holds the layout panels a GUI is
			//built out of, and a move reported against one of those would name a handle no script can match -
			//a control's handle is what Gui.controls is keyed on, and a panel is not in it.
			if (control == null
					|| !control.Properties.ContainsKey(hookedKey)
					|| control.Properties.ContainsKey(motionHookedKey))
				return;

			control.Properties[motionHookedKey] = true;
			control.MouseMove += OnMouseMove;
		}

		private static void DetachMotion(Forms.Control control)
		{
			if (control == null || !control.Properties.ContainsKey(motionHookedKey))
				return;

			_ = control.Properties.Remove(motionHookedKey);
			control.MouseMove -= OnMouseMove;
		}

#if LINUX
		[DllImport("libgtk-3.so.0")]
		private static extern uint gtk_get_current_event_time();

		private static uint claimedStamp;
		private static int claimedMsg;

		/// <summary>
		/// Whether this is the first report of the native event currently being dispatched, which is the only
		/// one that may be delivered: Windows sends a message to a single window, while GTK offers one event
		/// to the innermost widget and then to every hooked ancestor, and emits it TWICE on a toplevel (one
		/// connected handler, two calls — so nothing above the toolkit can prevent it). The innermost widget
		/// is offered the event first, which makes the first report the correct one.
		/// <para>
		/// Called only once a report is otherwise going to be made, so that one dropped for another reason —
		/// a key event offered to a window that does not hold the focus — leaves the event unclaimed for the
		/// control that should answer for it.
		/// </para>
		/// <para>
		/// Cocoa hit-tests to one view and does not propagate, so this correction is GTK's alone.
		/// </para>
		/// </summary>
		private static bool ClaimNativeEvent(int msg)
		{
			var stamp = gtk_get_current_event_time();

			//GDK_CURRENT_TIME: nothing is being dispatched, so there is no repeat to recognise.
			if (stamp == 0)
				return true;

			if (stamp == claimedStamp && msg == claimedMsg)
				return false;

			claimedStamp = stamp;
			claimedMsg = msg;
			return true;
		}

#else
		private static bool ClaimNativeEvent(int msg) => true;

#endif
		/// <summary>
		/// Whether this control is the innermost one holding the keyboard focus. Keys go to the focused
		/// control the way Windows sends them to the focused window, and GTK offers a key event to the
		/// toplevel before the focus widget, so a window has to stand aside while one of its controls holds
		/// the focus.
		/// </summary>
		private static bool IsFocusTarget(Forms.Control control)
			=> control.HasFocus && (control is not Forms.Container container || !HasFocusedDescendant(container));

		private static bool HasFocusedDescendant(Forms.Control parent)
		{
			foreach (var child in parent.VisualControls)
				if (child.HasFocus || HasFocusedDescendant(child))
					return true;

			return false;
		}

		private static void OnMouseDown(object sender, Forms.MouseEventArgs e)
			=> DispatchMouse(sender, e, WindowsAPI.WM_LBUTTONDOWN, WindowsAPI.WM_RBUTTONDOWN, WindowsAPI.WM_MBUTTONDOWN);

		private static void OnMouseUp(object sender, Forms.MouseEventArgs e)
			=> DispatchMouse(sender, e, WindowsAPI.WM_LBUTTONUP, WindowsAPI.WM_RBUTTONUP, WindowsAPI.WM_MBUTTONUP);

		private static void OnMouseDoubleClick(object sender, Forms.MouseEventArgs e)
			=> DispatchMouse(sender, e, WindowsAPI.WM_LBUTTONDBLCLK, WindowsAPI.WM_RBUTTONDBLCLK, WindowsAPI.WM_MBUTTONDBLCLK);

		private static void OnMouseMove(object sender, Forms.MouseEventArgs e)
		{
			if (sender is Forms.Control control
					&& ClaimNativeEvent(WindowsAPI.WM_MOUSEMOVE)
					&& Dispatch(control, WindowsAPI.WM_MOUSEMOVE, MouseKeyState(e), PackPoint(e.Location)))
				e.Handled = true;
		}

		private static void DispatchMouse(object sender, Forms.MouseEventArgs e, int left, int right, int middle)
		{
			if (sender is not Forms.Control control)
				return;

			var msg = e.Buttons switch
			{
				Forms.MouseButtons.Alternate => right,
				Forms.MouseButtons.Middle => middle,
				Forms.MouseButtons.Primary => left,
				_ => 0
			};

			if (msg != 0 && ClaimNativeEvent(msg) && Dispatch(control, msg, MouseKeyState(e), PackPoint(e.Location)))
				e.Handled = true;
		}

		private static void OnMouseWheel(object sender, Forms.MouseEventArgs e)
		{
			if (sender is not Forms.Control control || !ClaimNativeEvent(WindowsAPI.WM_MOUSEWHEEL))
				return;

			//WM_MOUSEWHEEL packs the notch count into the high word of wParam (WHEEL_DELTA units) and, unlike
			//every other mouse message, carries SCREEN rather than client coordinates in lParam. Eto reports
			//the delta in LINES, so one notch is however many lines the backend scrolls per notch.
#if LINUX
			//Eto's GTK backend scrolls this many lines per notch (its GtkControl.ScrollAmount, whose key is
			//internal to that assembly, so the default is restated rather than read). Keysharp never sets it.
			const float linesPerNotch = 2f;
#else
			const float linesPerNotch = 1f;
#endif
			var delta = (short)Math.Round(e.Delta.Height / linesPerNotch * 120f);
			var wparam = Pack32((delta << 16) | (int)MouseKeyState(e));
			var origin = control.ScreenOrigin();
			var screen = new PointF(origin.X + e.Location.X, origin.Y + e.Location.Y);

			if (Dispatch(control, WindowsAPI.WM_MOUSEWHEEL, wparam, PackPoint(screen)))
				e.Handled = true;
		}

		private static void OnKeyDown(object sender, Forms.KeyEventArgs e) => DispatchKey(sender, e, true);

		private static void OnKeyUp(object sender, Forms.KeyEventArgs e) => DispatchKey(sender, e, false);

		private static void DispatchKey(object sender, Forms.KeyEventArgs e, bool down)
		{
			if (sender is not Forms.Control control)
				return;

			var vk = ToVirtualKey(e.Key & Forms.Keys.KeyMask);

			if (vk == 0 || !IsFocusTarget(control))
				return;

			//A keystroke held with Alt down is a system key, exactly as Windows reports it.
			var alt = (e.Modifiers & Forms.Keys.Alt) == Forms.Keys.Alt;
			var msg = down
					  ? (alt ? WindowsAPI.WM_SYSKEYDOWN : WindowsAPI.WM_KEYDOWN)
					  : (alt ? WindowsAPI.WM_SYSKEYUP : WindowsAPI.WM_KEYUP);

			if (ClaimNativeEvent(msg) && Dispatch(control, msg, (nint)vk, KeyLParam(down, alt)))
				e.Handled = true;
		}

		private static void OnTextInput(object sender, Forms.TextInputEventArgs e)
		{
			if (sender is not Forms.Control control
					|| string.IsNullOrEmpty(e.Text)
					|| !IsFocusTarget(control)
					|| !ClaimNativeEvent(WindowsAPI.WM_CHAR))
				return;

			//One WM_CHAR per character: an input method can commit a whole phrase in a single Eto event.
			var handled = false;

			foreach (var ch in e.Text)
				handled |= Dispatch(control, WindowsAPI.WM_CHAR, (nint)ch, KeyLParam(true, false));

			if (handled)
				e.Cancel = true;
		}

		/// <summary>
		/// Runs the monitors for one message, mirroring the order KeysharpForm.WndProc uses on Windows: the
		/// global OnMessage() monitors first, then whatever the message was addressed to, each able to claim
		/// the message by returning a non-empty value.
		/// </summary>
		/// <returns>True when a monitor claimed the message, meaning default processing must be suppressed.</returns>
		private static bool Dispatch(Forms.Control control, int msg, nint wparam, nint lparam)
		{
			var script = OwningScript(control);

			if (script == null || script.IsDisposed)
				return false;

			var filter = script.msgFilter;

			if (filter == null)
				return false;

			var m = new Message
			{
				HWnd = control.Handle,
				Msg = msg,
				WParam = wparam,
				LParam = lparam,
				Result = 0
			};
			//Stashed the way the Windows pre-filter stashes a message it took off the queue, which is what
			//gives the monitor an A_EventInfo of when the input happened rather than a bare 0.
			filter.handledMsg = m;
			//Dispatched inline rather than queued, because a buffered monitor has already returned by the
			//time its result is known and so could never claim the message.
			return filter.CallEventHandlers(ref m) || InvokeOwnerHandlers(control, ref m);
		}

		/// <summary>
		/// Runs the OnMessage() monitors of whatever the message was addressed to. Windows keeps the two
		/// registrations apart because they subclass different windows — a message to the GUI window reaches
		/// Gui.OnMessage(), one to a control reaches that control's GuiCtrl.OnMessage() — so the target is
		/// resolved here rather than handing every message to the owning GUI.
		/// </summary>
		private static bool InvokeOwnerHandlers(Forms.Control control, ref Message m)
		{
			if (control is KeysharpForm)
				return OwningGui(control) is Builtins.Gui gui && gui.InvokeWindowMessageHandlers(ref m);

			return control.GetGuiControl() is Builtins.Gui.Control guiControl && guiControl.InvokeWindowMessageHandlers(ref m);
		}

		private static Builtins.Gui OwningGui(Forms.Control control)
		{
			for (var parent = control; parent != null; parent = parent.Parent)
				if (parent is KeysharpForm form && form.Tag is WeakReference<Builtins.Gui> weak && weak.TryGetTarget(out var gui))
					return gui;

			return null;
		}

		private static Script OwningScript(Forms.Control control)
		{
			for (var parent = control; parent != null; parent = parent.Parent)
				if (parent is KeysharpForm form)
					return form.OwnerScript;

			return null;
		}

		/// <summary>
		/// The MK_* bitset every mouse message carries in wParam.
		/// </summary>
		private static nint MouseKeyState(Forms.MouseEventArgs e)
		{
			var state = 0;

			if ((e.Buttons & Forms.MouseButtons.Primary) != 0) state |= WindowsAPI.MK_LBUTTON;
			if ((e.Buttons & Forms.MouseButtons.Alternate) != 0) state |= WindowsAPI.MK_RBUTTON;
			if ((e.Buttons & Forms.MouseButtons.Middle) != 0) state |= WindowsAPI.MK_MBUTTON;
			if ((e.Modifiers & Forms.Keys.Shift) == Forms.Keys.Shift) state |= WindowsAPI.MK_SHIFT;
			if ((e.Modifiers & Forms.Keys.Control) == Forms.Keys.Control) state |= WindowsAPI.MK_CONTROL;

			return state;
		}

		/// <summary>
		/// Widens a 32-bit message parameter the way Windows does. Both parameters are pointer-sized there, but
		/// the values are built as 32-bit words and land in the low half with the top half CLEAR — so a script
		/// that shifts a wheel delta or a transition bit out of one gets a positive number. Casting a negative
		/// int straight to nint would sign-extend instead and hand it a very different value off Windows.
		/// </summary>
		private static nint Pack32(int value) => (nint)(uint)value;

		/// <summary>
		/// Mouse lParam: x in the low word, y in the high word, both signed 16-bit.
		/// </summary>
		private static nint PackPoint(PointF point)
		{
			var x = (short)Math.Round(point.X);
			var y = (short)Math.Round(point.Y);
			return Pack32(((ushort)y << 16) | (ushort)x);
		}

		/// <summary>
		/// Key lParam: a repeat count of 1, the context bit that marks a system key, and for a release the
		/// previous-key-state and transition-state bits Windows always sets together on a WM_KEYUP. The scan
		/// code and extended-key bits are left clear — there is no scan code to report for a key that arrived
		/// through the toolkit rather than the hook.
		/// </summary>
		private static nint KeyLParam(bool down, bool alt)
		{
			var lparam = 1;

			if (alt)
				lparam |= 1 << 29;

			if (!down)
				lparam |= (1 << 30) | (1 << 31);

			return Pack32(lparam);
		}

		/// <summary>
		/// Maps an Eto key to its Windows virtual-key code. Eto's own values are a dense enum of its own
		/// making, so the ranges that are contiguous in both are converted arithmetically and the rest are
		/// listed. Returns 0 for a key with no Windows equivalent, which is not reported at all.
		/// </summary>
		private static uint ToVirtualKey(Forms.Keys key)
		{
			if (key >= Forms.Keys.A && key <= Forms.Keys.Z)
				return (uint)('A' + (key - Forms.Keys.A));

			if (key >= Forms.Keys.D0 && key <= Forms.Keys.D9)
				return (uint)('0' + (key - Forms.Keys.D0));

			if (key >= Forms.Keys.F1 && key <= Forms.Keys.F12)
				return VK_F1 + (uint)(key - Forms.Keys.F1);

			if (key >= Forms.Keys.F13 && key <= Forms.Keys.F24)
				return VK_F13 + (uint)(key - Forms.Keys.F13);

			if (key >= Forms.Keys.Keypad0 && key <= Forms.Keys.Keypad9)
				return VK_NUMPAD0 + (uint)(key - Forms.Keys.Keypad0);

			return key switch
			{
				Forms.Keys.Backspace => VK_BACK,
				Forms.Keys.Tab => VK_TAB,
				Forms.Keys.Clear => VK_CLEAR,
				Forms.Keys.Enter => VK_RETURN,
				Forms.Keys.Pause => VK_PAUSE,
				Forms.Keys.CapsLock => VK_CAPITAL,
				Forms.Keys.Escape => VK_ESCAPE,
				Forms.Keys.Space => VK_SPACE,
				Forms.Keys.PageUp => VK_PRIOR,
				Forms.Keys.PageDown => VK_NEXT,
				Forms.Keys.End => VK_END,
				Forms.Keys.Home => VK_HOME,
				Forms.Keys.Left => VK_LEFT,
				Forms.Keys.Up => VK_UP,
				Forms.Keys.Right => VK_RIGHT,
				Forms.Keys.Down => VK_DOWN,
				Forms.Keys.PrintScreen => VK_SNAPSHOT,
				Forms.Keys.Insert => VK_INSERT,
				Forms.Keys.Delete => VK_DELETE,
				Forms.Keys.Help => VK_HELP,
				Forms.Keys.Multiply => VK_MULTIPLY,
				Forms.Keys.Add => VK_ADD,
				Forms.Keys.Subtract => VK_SUBTRACT,
				Forms.Keys.Decimal => VK_DECIMAL,
				Forms.Keys.Divide => VK_DIVIDE,
				Forms.Keys.NumberLock => VK_NUMLOCK,
				Forms.Keys.ScrollLock => VK_SCROLL,
				Forms.Keys.LeftShift => VK_LSHIFT,
				Forms.Keys.RightShift => VK_RSHIFT,
				Forms.Keys.LeftControl => VK_LCONTROL,
				Forms.Keys.RightControl => VK_RCONTROL,
				Forms.Keys.LeftAlt => VK_LMENU,
				Forms.Keys.RightAlt => VK_RMENU,
				Forms.Keys.LeftApplication => VK_LWIN,
				Forms.Keys.RightApplication => VK_RWIN,
				Forms.Keys.ContextMenu => VK_APPS,
				Forms.Keys.Semicolon => VK_OEM_1,
				Forms.Keys.Equal => VK_OEM_PLUS,
				Forms.Keys.Comma => VK_OEM_COMMA,
				Forms.Keys.Minus => VK_OEM_MINUS,
				Forms.Keys.Period => VK_OEM_PERIOD,
				Forms.Keys.Slash => VK_OEM_2,
				Forms.Keys.Grave => VK_OEM_3,
				Forms.Keys.LeftBracket => VK_OEM_4,
				Forms.Keys.Backslash => VK_OEM_5,
				Forms.Keys.RightBracket => VK_OEM_6,
				Forms.Keys.Quote => VK_OEM_7,
				_ => 0u
			};
		}
	}
}
#endif
