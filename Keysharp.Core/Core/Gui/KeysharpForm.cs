namespace Keysharp.Core
{
	public class KeysharpForm : Form
	{
		public bool AllowShowDisplay = true;
		internal List<IFuncObj> closedHandlers;
		internal List<IFuncObj> contextMenuChangedHandlers;
		internal List<IFuncObj> dropFilesHandlers;
		internal List<IFuncObj> escapeHandlers;
		internal object eventObj;
		internal bool showWithoutActivation;
		internal List<IFuncObj> sizeHandlers;
		private readonly int addStyle, addExStyle, removeStyle, removeExStyle;
		internal bool beenShown = false;
		internal bool beenConstructed = false;
		private bool closingFromDestroy;
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
			base.CreateHandle();
			beenConstructed = true;
		}

		protected override void WndProc(ref Message m)
		{
			// In Windows queued messages (eg sent with PostMessage) arrive in the message queue and
			// are read with GetMessage, then when DispatchMessage is called (after TranslateMessage)
			// WndProc is called with the translated message. Non-queued message (eg sent with SendMessage)
			// arrive directly in WndProc.
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
			var msgFilter = TheScript.msgFilter;

			if (msgFilter.handledMsg == m)
				msgFilter.handledMsg = null;
			else if (beenConstructed && msgFilter.CallEventHandlers(ref m))
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
			AutoScaleDimensions = new SizeF(96F, 96F);
			AutoScaleMode = AutoScaleMode.Dpi;
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
				Resize += Form_Resize;
				VisibleChanged += Form_VisibleChanged;
#else
				Closing += Form_FormClosing;
				SizeChanged += Form_Resize;
				Shown += Form_VisibleChanged;
#endif
				DragDrop += Form_DragDrop;
				KeyDown += Form_KeyDown;
				MouseDown += Form_MouseDown;
			}

			Shown += (o, e) => beenShown = true;
		}

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
				script.mainWindow?.CheckedBeginInvoke(new Action(() => GC.Collect()), true, true);
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

					if (Script.ForceLong(result) != 0L)
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
			if ((e.Key == Forms.Keys.Application || (e.Key == Forms.Keys.F10 && ((e.Modifiers & Forms.Keys.Shift) == Forms.Keys.Shift))) && GetCursorPos(out POINT pt))
				CallContextMenuChangeHandlers(true, pt.X, pt.Y);
			else if (e.Key == Forms.Keys.Escape && Tag is WeakReference<Gui> wrg && wrg.TryGetTarget(out var g))
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
				CallContextMenuChangeHandlers(false, e.Location.X.Ai(), e.Location.Y.Ai());
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

				if (g.dpiscaling)
					_ = sizeHandlers?.InvokeEventHandlers(g, state, (long)(client.Width / A_ScaledScreenDPI), (long)(client.Height / A_ScaledScreenDPI));
				else
					_ = sizeHandlers?.InvokeEventHandlers(g, state, (long)client.Width, (long)client.Height);
			}

			UpdateStatusStripLayout();
		}

		internal object OnEvent(object obj0, object obj1, object obj2 = null)
		{
			var e = obj0.As();
			var h = obj1;
			var i = obj2.Al(1);
			e = e.ToLower();
			var del = Functions.GetFuncObj(h, eventObj, true);
			if (!((FuncObj)del).IsClosure)
				del.Inst = null;

			if (e == "close")
			{
				if (closedHandlers == null)
					closedHandlers = [];

				closedHandlers.ModifyEventHandlers(del, i);
			}
			else if (e == "contextmenu")
			{
				if (contextMenuChangedHandlers == null)
					contextMenuChangedHandlers = [];

				contextMenuChangedHandlers.ModifyEventHandlers(del, i);
			}
			else if (e == "dropfiles")
			{
				if (dropFilesHandlers == null)
					dropFilesHandlers = [];

				dropFilesHandlers.ModifyEventHandlers(del, i);
			}
			else if (e == "escape")
			{
				if (escapeHandlers == null)
					escapeHandlers = [];

				escapeHandlers.ModifyEventHandlers(del, i);
			}
			else if (e == "size")
			{
				if (sizeHandlers == null)
					sizeHandlers = [];

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
					Script.TheScript.GuiData.allGuiHwnds[this.Handle.ToInt64()] = g;

				UpdateStatusStripLayout();
			}
			else
				ClearThis(false);
		}

		internal void UpdateStatusStripLayout()
		{
#if !WINDOWS
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
