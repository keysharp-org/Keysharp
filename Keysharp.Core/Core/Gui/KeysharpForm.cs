namespace Keysharp.Core;

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

	protected override CreateParams CreateParams
	{
		get
		{
			var cp = base.CreateParams;
			cp.ExStyle |= 0x02000000; // Add WS_EX_COMPOSITED
			cp.Style |= addStyle;
			cp.ExStyle |= addExStyle;
			cp.Style &= ~removeStyle;
			cp.ExStyle &= ~removeExStyle;
			return cp;
		}
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
		else if (msgFilter.CallEventHandlers(ref m))
			return;

		base.WndProc(ref m);
	}

	[Browsable(false)]
	protected override bool ShowWithoutActivation => showWithoutActivation;

	public KeysharpForm(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
	{
		addStyle = _addStyle;
		addExStyle = _addExStyle;
		removeStyle = _removeStyle;
		removeExStyle = _removeExStyle;
		AutoScaleDimensions = new SizeF(96F, 96F);
		AutoScaleMode = AutoScaleMode.Dpi;
		//See Gui.Show() for where the remainder of the properties get set, such as scaling values.
		Font = MainWindow.OurDefaultFont;
		StartPosition = FormStartPosition.CenterScreen;
		KeyPreview = true;
		DoubleBuffered = true;
		SetStyle(ControlStyles.StandardClick, true);
		SetStyle(ControlStyles.StandardDoubleClick, true);

		if (this is not MainWindow)
		{
			FormClosing += Form_FormClosing;
			DragDrop += Form_DragDrop;
			KeyDown += Form_KeyDown;
			MouseDown += Form_MouseDown;
			Resize += Form_Resize;
			VisibleChanged += Form_VisibleChanged;
		}

		Shown += (o, e) => beenShown = true;
	}

	internal void CallContextMenuChangeHandlers(bool wasRightClick, int x, int y)
	{
		if (Tag is WeakReference<Gui> wrg && wrg.TryGetTarget(out var g))
		{
			var control = ActiveControl;

			if (control is ListBox lb)
				_ = (contextMenuChangedHandlers?.InvokeEventHandlers(g, control, lb.SelectedIndex + 1L, wasRightClick, (long)x, (long)y));
			else if (control is ListView lv)
				_ = (contextMenuChangedHandlers?.InvokeEventHandlers(g, control, lv.SelectedIndices.Count > 0 ? lv.SelectedIndices[0] + 1L : 0L, wasRightClick, (long)x, (long)y));
			else if (control is TreeView tv)
				_ = (contextMenuChangedHandlers?.InvokeEventHandlers(g, control, tv.SelectedNode.Handle, wasRightClick, (long)x, (long)y));
			else
				_ = (contextMenuChangedHandlers?.InvokeEventHandlers(g, control, control != null ? control.Handle.ToInt64().ToString() : "", wasRightClick, (long)x, (long)y));//Unsure what to pass for Item, so just pass handle.
		}
	}

	internal void ClearThis()
	{
		//This will be called when a window is either hidden or destroyed. In both cases,
		//we must check if there are any remaining visible windows. If not, and the script
		//has not been explicitly marked persistent, then exit the program.
		var handle = Handle.ToInt64();
		var script = Script.TheScript;
		_ = script.GuiData.allGuiHwnds.TryRemove(handle, out _);
		script.mainWindow?.CheckedBeginInvoke(new Action(() => GC.Collect()), true, true);
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
		if (e.Data.GetDataPresent(DataFormats.FileDrop) && Tag is WeakReference<Gui> wrg && wrg.TryGetTarget(out var g))
		{
			var coords = PointToClient(new Point(e.X, e.Y));
			var files = (string[])e.Data.GetData(DataFormats.FileDrop);
			_ = dropFilesHandlers?.InvokeEventHandlers(g, ActiveControl, new Array(files), coords.X, coords.Y);
		}
	}

	internal void Form_FormClosing(object sender, FormClosingEventArgs e)
	{
		if (Tag is WeakReference<Gui> wrg && wrg.TryGetTarget(out var g))//This will be null when the form is actually being destroyed.
		{
			if (!closingFromDestroy)
			{
				var result = closedHandlers?.InvokeEventHandlers(g);
				e.Cancel = true;

				if (Script.ForceLong(result) != 0L)
					return;

				Hide();
			}
			else
			{
				ClearThis();
			}
		}
	}

	internal void Form_KeyDown(object sender, KeyEventArgs e)
	{
		if (e.KeyCode == Keys.Apps || (e.KeyCode == Keys.F10 && ((ModifierKeys & Keys.Shift) == Keys.Shift)))
			CallContextMenuChangeHandlers(true, Cursor.Position.X, Cursor.Position.Y);
		else if (e.KeyCode == Keys.Escape && Tag is WeakReference<Gui> wrg && wrg.TryGetTarget(out var g))
			_ = escapeHandlers?.InvokeEventHandlers(g);
	}

	internal void Form_MouseDown(object sender, MouseEventArgs e)
	{
		if (e.Button == MouseButtons.Right)
			CallContextMenuChangeHandlers(false, e.X, e.Y);
	}

	internal void Form_Resize(object sender, EventArgs e)
	{
		if (Tag is WeakReference<Gui> wrg && wrg.TryGetTarget(out var g))
		{
			var state = WindowState == FormWindowState.Maximized ? 1L : WindowState == FormWindowState.Minimized ? -1L : 0L;

			var client = ClientSize;

			_ = g.dpiscaling
				? (sizeHandlers?.InvokeEventHandlers(g, state, (long)(client.Width / A_ScaledScreenDPI), (long)(client.Height / A_ScaledScreenDPI)))
				: (sizeHandlers?.InvokeEventHandlers(g, state, (long)client.Width, (long)client.Height));
		}
	}

	internal object OnEvent(object obj0, object obj1, object obj2 = null)
	{
		var e = obj0.As();
		var h = obj1;
		var i = obj2.Al(1);
		e = e.ToLower();
		var del = Functions.GetFuncObj(h, eventObj, true);

		if (e == "close")
		{
			closedHandlers ??= [];

			closedHandlers.ModifyEventHandlers(del, i);
		}
		else if (e == "contextmenu")
		{
			contextMenuChangedHandlers ??= [];

			contextMenuChangedHandlers.ModifyEventHandlers(del, i);
		}
		else if (e == "dropfiles")
		{
			dropFilesHandlers ??= [];

			dropFilesHandlers.ModifyEventHandlers(del, i);
		}
		else if (e == "escape")
		{
			escapeHandlers ??= [];

			escapeHandlers.ModifyEventHandlers(del, i);
		}
		else if (e == "size")
		{
			sizeHandlers ??= [];

			sizeHandlers.ModifyEventHandlers(del, i);
		}

		return DefaultObject;
	}

	protected override void SetVisibleCore(bool value) => base.SetVisibleCore(AllowShowDisplay ? value : AllowShowDisplay);

	private void Form_VisibleChanged(object sender, EventArgs e)
	{
		if (Visible)
		{
			if (Tag is WeakReference<Gui> wrg && wrg.TryGetTarget(out var g))
				Script.TheScript.GuiData.allGuiHwnds[Handle.ToInt64()] = g;
		}
		else
			ClearThis();
	}
}