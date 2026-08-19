using Keysharp.Runtime;
using CallbackHub = Keysharp.Internals.Scripting.CallbackRegistry<Keysharp.Internals.Scripting.CallbackRegistration>;

namespace Keysharp.Builtins
{
	public class KeysharpForm : Form
	{
		public bool AllowShowDisplay = true;
		internal CallbackHub closedHandlers;
		internal CallbackHub contextMenuChangedHandlers;
		internal CallbackHub dpiChangeHandlers;
		internal CallbackHub dropFilesHandlers;
		internal CallbackHub escapeHandlers;
		internal object eventObj;
		internal bool showWithoutActivation;
		internal CallbackHub sizeHandlers;
		private readonly int addStyle, addExStyle, removeStyle, removeExStyle;
		internal bool beenShown = false;
		internal bool beenConstructed = false;
		internal bool clickThrough;
		private bool closingFromDestroy;
#if !WINDOWS
		private long lastDpi = 96;
#endif
#if OSX
		private uint mouseTransparentWindow;
#endif
		internal bool BeenShown => beenShown;

#if WINDOWS
		protected override CreateParams CreateParams
		{
			get
			{
				var cp = base.CreateParams;
				//cp.ExStyle |= 0x02000000; // Add WS_EX_COMPOSITED
				cp.Style |= addStyle;
				cp.ExStyle |= addExStyle;
				cp.Style &= ~removeStyle;
				cp.ExStyle &= ~removeExStyle;
				return cp;
			}
		}

		protected override void CreateHandle()
		{
			if (IsDisposed || IsHandleCreated) return;
			base.CreateHandle();
			beenConstructed = true;
		}

		protected override void WndProc(ref Message m)
		{
			// In Windows queued messages (eg sent with PostMessage) arrive in the message queue and
			// are read with GetMessage, then when DispatchMessage is called (after TranslateMessage)
			// WndProc is called with the translated message. Non-queued message (eg sent with SendMessage)
			// arrive directly in WndProc, but also some queued messages such as from a modal message loop.
			// In C# MessageFilter processes the message after GetMessage has received it, and if let through
			// then TranslateMessage and DispatchMessage are called, which then in turn call WndProc.
			// The problem is how to determine whether a message has already been processed in MessageFilter to
			// avoid double-handing. AutoHotkey uses a global variable before DispatchMessage and nulls it
			// afterwards, and we use a similar approach here. MessageFilter stashes the handled
			// message (only messages which target a KeysharpForm) and then we compare here for
			// equality. A simple boolean like isHandled wouldn't be enough because for example
			// WM_KEYDOWN will get translated to WM_CHAR and the user may want to capture that as well.
			// Additionally if any messages get lost for some reason or another message arrives here
			// before the MessageFilter processed message has had time to arrive then we'd confuse the two.
			// A form can outlive the script that created it (the close is posted to the owning thread and only
			// runs at the next pump, by which time the script may be disposed and another one constructed),
			// so the filter can legitimately be absent here. Fall through to default processing when it is.
			var msgFilter = TheScript?.msgFilter;

			if (m.Msg == WindowsAPI.WM_COMMNOTIFY)
				_ = Dialogs.HandleDialogNotification((uint)m.WParam.ToInt64(), m.LParam);

			if (msgFilter != null)
			{
				if (msgFilter.handledMsg == m)
					msgFilter.handledMsg = null;
				else if (beenConstructed && msgFilter.CallEventHandlers(ref m))
					return;
			}

			//GuiObj.OnMessage() monitors run ahead of default processing, the same way AHK's GuiWindowProc
			//consults its message monitors before handing off to DefDlgProc.
			if (beenConstructed && Tag is WeakReference<Gui> guiRef && guiRef.TryGetTarget(out var owningGui)
					&& owningGui.InvokeWindowMessageHandlers(ref m))
				return;

			base.WndProc(ref m);
		}

		[Browsable(false)]
		protected override bool ShowWithoutActivation => showWithoutActivation;
#else
		[Browsable(false)]
		protected bool ShowWithoutActivation => showWithoutActivation;

		[Browsable(false)]
		protected new bool ShowActivated => !showWithoutActivation;

		public override bool Visible
		{
			get => base.Visible;
			set
			{
				var prev = base.Visible;
				base.Visible = value;
				if (beenShown && !value && prev != value)
					Form_VisibleChanged(this, EventArgs.Empty);
			}
		}
#endif

		public KeysharpForm(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
			addStyle = _addStyle;
			addExStyle = _addExStyle;
			removeStyle = _removeStyle;
			removeExStyle = _removeExStyle;
#if WINDOWS
			//Keysharp does all of its own DPI scaling (Gui.DpiScale on the way in, Gui.RescaleForDpi when the DPI
			//changes), so WinForms must not scale as well: it would fight the raw pixels a "-DPIScale" GUI supplies,
			//and it rescales every child unconditionally, which cannot honor a per-control "-DPIResize".
			AutoScaleMode = AutoScaleMode.None;
			//See Gui.Show() for where the remainder of the properties get set, such as scaling values.
			Font = MainWindow.OurDefaultFont;
			StartPosition = FormStartPosition.CenterScreen;
			KeyPreview = true;
			DoubleBuffered = true;
			SetStyle(ControlStyles.StandardClick, true);
			SetStyle(ControlStyles.StandardDoubleClick, true);
#else
			this.Font = MainWindow.OurDefaultFont;
#endif

            if (this is not MainWindow)
			{
#if WINDOWS
				FormClosing += Form_FormClosing;
				DpiChanged += Form_DpiChanged;
				Resize += Form_Resize;
				VisibleChanged += Form_VisibleChanged;
#else
				Closing += Form_FormClosing;
				LogicalPixelSizeChanged += Form_LogicalPixelSizeChanged;
				SizeChanged += Form_Resize;
				Shown += Form_VisibleChanged;
#endif
				DragDrop += Form_DragDrop;
				KeyDown += Form_KeyDown;
				MouseDown += Form_MouseDown;
#if !WINDOWS

				//On X11 the handle a window answers with changes the first time it is realized (widget pointer
				//before, XID after), so the key it is registered under has to follow. Re-homed at realize
				//rather than at Shown because the two are not adjacent: in between, a window query from another
				//thread enumerates, gets the XID, and fails to recognise a window of ours still keyed by the
				//pointer - and that lookup gates most of the Linux window service.
				this.OnRealized(() =>
				{
					if (Tag is WeakReference<Gui> realizedGui && realizedGui.TryGetTarget(out var rg))
						Register(rg);
				});

#endif
			}

			Shown += (o, e) =>
			{
				beenShown = true;
#if !WINDOWS
				lastDpi = CurrentDpi;
				_ = this.Handle;
				// On Wayland the titlebar/taskbar icon is resolved from the window's app_id (matched to an
				// installed keysharp.desktop), not from the Window.Icon pixbuf — GTK3 has no per-window icon
				// protocol there, so that pixbuf only feeds X11's _NET_WM_ICON. Tag the window with our
				// app_id so the compositor shows the Keysharp logo. The GdkWindow may still be unmapped at
				// Shown on Wayland, so retry once the map has settled.
				if (!Eto.Forms.EtoExtensions.SetWaylandAppId(this, "keysharp"))
					Eto.Forms.Application.Instance.AsyncInvoke(() => Eto.Forms.EtoExtensions.SetWaylandAppId(this, "keysharp"));
				// The GTK input shape needs a realized GdkWindow, which only exists once the window has
				// been shown, so (re)apply any requested click-through here.
				if (clickThrough)
				{
					ApplyClickThrough(true);
					// On Wayland the GdkWindow is often still unmapped at Shown (so the call above no-ops)
					// and the input region is pushed to the surface only on a later frame, so reapply once
					// the map has settled.
					Eto.Forms.Application.Instance.AsyncInvoke(() =>
					{
						if (clickThrough)
							ApplyClickThrough(true);
					});
				}
#endif
			};
#if OSX
			Closed += (o, e) => ApplyClickThrough(false);
#endif
		}

		// Makes the whole window transparent to mouse input so clicks pass through to whatever is beneath
		// it. Combine with a transparent background (e.g. WinSetTransColor on Windows) for a visible but
		// non-interactive overlay such as a region highlight. Exposed to scripts as the Gui
		// "+ClickThrough" / "-ClickThrough" option.
		internal void SetClickThrough(bool enable)
		{
			clickThrough = enable;
#if WINDOWS
			if (!IsHandleCreated)
				_ = Handle;

			var ex = WindowsAPI.GetWindowLongPtr(Handle, WindowsAPI.GWL_EXSTYLE).ToInt64();

			if (enable)
			{
				// WS_EX_LAYERED keeps the window composited so the DWM hit-tests transparent pixels
				// correctly; WS_EX_TRANSPARENT passes the mouse through. Together they are the standard
				// click-through overlay recipe.
				var wasLayered = (ex & WindowsAPI.WS_EX_LAYERED) != 0;
				_ = WindowsAPI.SetWindowLongPtr(Handle, WindowsAPI.GWL_EXSTYLE,
						new nint(ex | WindowsAPI.WS_EX_LAYERED | WindowsAPI.WS_EX_TRANSPARENT));

				// Turning on WS_EX_LAYERED for a window with no layered attributes leaves it blank, so seed
				// it fully opaque; a later WinSetTransColor/WinSetTransparent overrides this.
				if (!wasLayered)
					_ = WindowsAPI.SetLayeredWindowAttributes(Handle, 0, 255, (uint)WindowsAPI.LWA_ALPHA);
			}
			else
			{
				// Only drop WS_EX_TRANSPARENT; leave WS_EX_LAYERED intact so any transparency applied via
				// WinSetTransColor/WinSetTransparent keeps working.
				_ = WindowsAPI.SetWindowLongPtr(Handle, WindowsAPI.GWL_EXSTYLE,
						new nint(ex & ~(long)WindowsAPI.WS_EX_TRANSPARENT));
			}
#else
			// Eto exposes no cross-platform click-through option, so reach the native window. The GTK
			// input shape only exists once the window is realized, so this is also reapplied from Shown.
			ApplyClickThrough(enable);
#endif
		}

#if !WINDOWS
		private void ApplyClickThrough(bool enable)
		{
			Eto.Forms.EtoExtensions.SetFormClickThrough(this, enable);
#if OSX
			var current = enable && ControlObject is MonoMac.AppKit.NSWindow window && window.WindowNumber > 0
				? (uint)window.WindowNumber : 0;
			if (mouseTransparentWindow != 0 && current != mouseTransparentWindow)
				MacNativeWindows.SetMouseTransparentWindow(mouseTransparentWindow, false);
			mouseTransparentWindow = current;
			if (current != 0)
				MacNativeWindows.SetMouseTransparentWindow(current, true);
#endif
		}
#endif

		internal bool RemoveOwnedHandlers(ScriptEventScheduler scheduler)
			=> (closedHandlers?.RemoveOwned(scheduler) == true)
				| (contextMenuChangedHandlers?.RemoveOwned(scheduler) == true)
				| (dpiChangeHandlers?.RemoveOwned(scheduler) == true)
				| (dropFilesHandlers?.RemoveOwned(scheduler) == true)
				| (escapeHandlers?.RemoveOwned(scheduler) == true)
				| (sizeHandlers?.RemoveOwned(scheduler) == true);

        internal void CallContextMenuChangeHandlers(bool wasRightClick, int x, int y)
		{
			if (Tag is WeakReference<Gui> wrg && wrg.TryGetTarget(out var g))
			{
				var control = this.ActiveControl;

				if (control is ListBox lb)
					_ = (contextMenuChangedHandlers?.InvokeEventHandlers(g, control, lb.SelectedIndex + 1L, wasRightClick, (long)x, (long)y));
				else if (control is KeysharpListView lv)
					_ = (contextMenuChangedHandlers?.InvokeEventHandlers(g, control, lv.SelectedIndices.Count > 0 ? lv.SelectedIndices[0] + 1L : 0L, wasRightClick, (long)x, (long)y));
				else if (control is KeysharpTreeView tv)
					_ = (contextMenuChangedHandlers?.InvokeEventHandlers(g, control, tv.SelectedNode.Handle, wasRightClick, (long)x, (long)y));
				else
					_ = (contextMenuChangedHandlers?.InvokeEventHandlers(g, control, control != null ? control.Handle.ToInt64().ToString() : "", wasRightClick, (long)x, (long)y));//Unsure what to pass for Item, so just pass handle.
			}
		}

		//The key this window is registered under, so a re-home drops the old one instead of searching for it:
		//on X11 the handle changes the first time the window is realized.
		private long registeredHwnd;

		/// <summary>Points allGuiHwnds at this window's current handle, dropping the key it last used.</summary>
		internal void Register(Gui gui)
		{
			var allGuiHwnds = Script.TheScript.GuiData.allGuiHwnds;
			var handle = this.Handle.ToInt64();

			if (registeredHwnd != 0 && registeredHwnd != handle)
				_ = allGuiHwnds.TryRemove(registeredHwnd, out _);

			allGuiHwnds[handle] = gui;
			registeredHwnd = handle;
		}

		internal void ClearThis(bool isClosing = true)
		{
			//This will be called when a window is either hidden or destroyed. In both cases,
			//we must check if there are any remaining visible windows. If not, and the script
			//has not been explicitly marked persistent, then exit the program.
			var handle = this.Handle.ToInt64();
			var script = Script.TheScript;
			if (isClosing)
			{
				_ = script.GuiData.allGuiHwnds.TryRemove(handle, out _);
				registeredHwnd = 0;
				Script.PostToUIThread(GC.Collect);
			}
			script.ExitIfNotPersistent();//Also does BeginInvoke(), so it will come after the GC.Collect() above.
		}

		internal object Destroy()
		{
			closingFromDestroy = true;

			//Do not close the window if the program is already exiting because it will throw
			//an enumeration modified exception because Winforms is internally already iterating over
			//all open windows to close them.
			if (!Script.TheScript.IsMainWindowClosing)
				this.CheckedInvoke(Close, false);

			return DefaultObject;
		}

		internal void Form_DragDrop(object sender, DragEventArgs e)
		{
#if WINDOWS
			if (e.Data.GetDataPresent(DataFormats.FileDrop) && Tag is WeakReference<Gui> wrg && wrg.TryGetTarget(out var g))
			{
				var coords = PointToClient(new Point(e.X, e.Y));
				var files = (string[])e.Data.GetData(DataFormats.FileDrop);
				_ = dropFilesHandlers?.InvokeEventHandlers(g, ActiveControl, new Array(files), coords.X, coords.Y);
			}
#else
			if (e.Data.ContainsUris && Tag is WeakReference<Gui> wrg && wrg.TryGetTarget(out var g))
			{
				var coords = PointFromScreen(e.Location);
				var files = (string[])e.Data.Uris.Select(uri => uri.ToString());
				_ = dropFilesHandlers?.InvokeEventHandlers(g, sender, new Array(files), coords.X, coords.Y);
			}
#endif
		}

#if WINDOWS
		internal void Form_FormClosing(object sender, FormClosingEventArgs e)
#else
		internal void Form_FormClosing(object sender, CancelEventArgs e)
#endif
		{
			if (Tag is WeakReference<Gui> wrg && wrg.TryGetTarget(out var g))//This will be null when the form is actually being destroyed.
			{
				if (!closingFromDestroy)
				{
					var result = closedHandlers?.InvokeEventHandlers(g);
					e.Cancel = true;

					if (result.Al() != 0L)
						return;

					this.Hide();
				}
				else
				{
					ClearThis();
				}
			}
		}

		internal void Form_KeyDown(object sender, KeyEventArgs e)
		{
#if WINDOWS
			if ((e.KeyCode == Keys.Apps || (e.KeyCode == Keys.F10 && ((ModifierKeys & Keys.Shift) == Keys.Shift))) && GetCursorPos(out POINT pt))
				CallContextMenuChangeHandlers(true, pt.X, pt.Y);
			else if (e.KeyCode == Keys.Escape && Tag is WeakReference<Gui> wrg && wrg.TryGetTarget(out var g))
				_ = escapeHandlers?.InvokeEventHandlers(g);
#else
#if !OSX
			// The Menu/context-menu key and Shift+F10 open the context menu on Windows and Linux.
			// macOS has no such key (and uses Ctrl+click, handled in Form_MouseDown), so it is omitted there.
			if ((e.Key == Forms.Keys.ContextMenu || (e.Key == Forms.Keys.F10 && ((e.Modifiers & Forms.Keys.Shift) == Forms.Keys.Shift))) && GetCursorPos(out POINT pt))
				CallContextMenuChangeHandlers(true, pt.X, pt.Y);
			else
#endif
			if (e.Key == Forms.Keys.Escape && Tag is WeakReference<Gui> wrg && wrg.TryGetTarget(out var g))
				_ = escapeHandlers?.InvokeEventHandlers(g);
#endif
		}

		internal void Form_MouseDown(object sender, MouseEventArgs e)
		{
#if WINDOWS
			if (e.Button == MouseButtons.Right)
				CallContextMenuChangeHandlers(false, e.X, e.Y);
#else
				if (e.Buttons == MouseButtons.Alternate)
					CallContextMenuChangeHandlers(false, Convert.ToInt32(e.Location.X), Convert.ToInt32(e.Location.Y));
#endif
			}

		internal void Form_Resize(object sender, EventArgs e)
		{
			if (Tag is WeakReference<Gui> wrg && wrg.TryGetTarget(out var g))
			{
				long state;

				if (WindowState == FormWindowState.Maximized)
					state = 1L;
				else if (WindowState == FormWindowState.Minimized)
					state = -1L;
				else
					state = 0L;

				Size client = ClientSize;

				if (g.DpiScale != 1.0)
					_ = sizeHandlers?.InvokeEventHandlers(g, state, (long)(client.Width / g.DpiScale), (long)(client.Height / g.DpiScale));
				else
					_ = sizeHandlers?.InvokeEventHandlers(g, state, (long)client.Width, (long)client.Height);
			}

			UpdateStatusStripLayout();
#if !WINDOWS
			// A resize on Wayland can drop the click-through input region (e.g. the Highlight overlay
			// resizes on every update), so reapply it.
			if (clickThrough)
				ApplyClickThrough(true);
#endif
		}

#if WINDOWS
		private void Form_DpiChanged(object sender, DpiChangedEventArgs e)
		{
			//Only the controls are re-laid out here; the window itself is left to WinForms, which applies the
			//suggested rectangle once this returns.
			if (Tag is WeakReference<Gui> wrg && wrg.TryGetTarget(out var g))
				g.RescaleForDpi(e.DeviceDpiOld, e.DeviceDpiNew);

			CallDpiChangeHandlers(e.DeviceDpiOld, e.DeviceDpiNew);
		}
#else
		private long CurrentDpi => Math.Max(1L, (long)Math.Round(LogicalPixelSize * 96.0));

		private void Form_LogicalPixelSizeChanged(object sender, EventArgs e)
		{
			var newDpi = CurrentDpi;
			var oldDpi = lastDpi;
			lastDpi = newDpi;

			if (oldDpi != newDpi)
				CallDpiChangeHandlers(oldDpi, newDpi);
		}
#endif

		internal void CallDpiChangeHandlers(long oldDpi, long newDpi)
		{
			if (Tag is WeakReference<Gui> wrg && wrg.TryGetTarget(out var g))
				_ = dpiChangeHandlers?.InvokeEventHandlers(g, oldDpi, newDpi);
		}

		internal object OnEvent(object obj0, object obj1, object obj2 = null)
		{
			var e = obj0.As();
			var h = obj1;
			var i = obj2.Al(1);
			e = e.ToLower();
			var del = Functions.GetKeysharpFunc(h, eventObj, true);

			// ModifyEventHandlers ignores a null delegate, so a callback that did not resolve would otherwise
			// register nothing and still report success.
			if (del == null)
				return Errors.ValueErrorOccurred("The callback was not a valid function.");

			// Only detach the receiver GetKeysharpFunc just attached by resolving a method NAME on the sink, since
			// the Gui takes the receiver slot at dispatch. A function object the script supplied carries its own
			// receiver, and is the script's own object, so clearing Inst on it would corrupt every other holder.
			if (h is string && eventObj != null && ReferenceEquals(del?.Inst, eventObj))
				del.Inst = null;

			if (e == "close")
			{
				if (closedHandlers == null)
					closedHandlers = new();

				closedHandlers.ModifyEventHandlers(del, i);
			}
			else if (e == "contextmenu")
			{
				if (contextMenuChangedHandlers == null)
					contextMenuChangedHandlers = new();

				contextMenuChangedHandlers.ModifyEventHandlers(del, i);
			}
			else if (e == "dropfiles")
			{
				if (dropFilesHandlers == null)
					dropFilesHandlers = new();

				dropFilesHandlers.ModifyEventHandlers(del, i);
			}
			else if (e is "dpichange" or "dpichanged")
			{
				if (dpiChangeHandlers == null)
					dpiChangeHandlers = new();

				dpiChangeHandlers.ModifyEventHandlers(del, i);
			}
			else if (e == "escape")
			{
				if (escapeHandlers == null)
					escapeHandlers = new();

				escapeHandlers.ModifyEventHandlers(del, i);
			}
			else if (e == "size")
			{
				if (sizeHandlers == null)
					sizeHandlers = new();

				sizeHandlers.ModifyEventHandlers(del, i);
			}

			return DefaultObject;
		}

#if WINDOWS
		protected override void SetVisibleCore(bool value)
		{
			base.SetVisibleCore(AllowShowDisplay ? value : AllowShowDisplay);
		}
#endif

        private void Form_VisibleChanged(object sender, EventArgs e)
		{
			if (Visible)
			{
				if (Tag is WeakReference<Gui> wrg && wrg.TryGetTarget(out var g))
				{
					Register(g);
				}

				UpdateStatusStripLayout();
			}
			else
				ClearThis(false);
		}

		internal void UpdateStatusStripLayout()
		{
#if !WINDOWS
			if (IsDisposed || Content == null)
				return;

			KeysharpStatusStrip statusStrip = null;

			foreach (var ctrl in Content.Controls)
			{
				if (ctrl is KeysharpStatusStrip ss && ss.Visible)
				{
					statusStrip = ss;
					break;
				}
			}

			if (statusStrip == null)
				return;

			var client = ClientSize;
			var currentSize = statusStrip.GetSize();
			var height = currentSize.Height < 0 ? 1 : currentSize.Height;
			var padding = this.Padding;
			var width = Math.Max(1, client.Width);

			statusStrip.SetSize(new Size(width, height));
			statusStrip.SetLocation(new Point(0, client.Height - height));
#endif
		}

	}
}
