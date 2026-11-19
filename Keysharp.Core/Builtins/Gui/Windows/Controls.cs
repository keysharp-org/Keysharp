#if WINDOWS
using Label = System.Windows.Forms.Label;

namespace Keysharp.Builtins
{
	/// <summary>
	/// The window styles of a Gui's controls, set in CreateParams since WinForms makes a window again from those.
	/// </summary>
	internal static class CreateParamsExtensions
	{
		/// <summary>
		/// Removes the selected clipping styles before the script's explicit styles are applied.
		/// </summary>
		internal static CreateParams Unclipped(this CreateParams cp, int clipStyles = WindowsAPI.WS_CLIPSIBLINGS)
		{
			cp.Style &= ~clipStyles;
			return cp;
		}

		/// <summary>Adds and removes the styles given in a control's options.</summary>
		internal static CreateParams WithStyles(this CreateParams cp, int addStyle, int removeStyle, int addExStyle, int removeExStyle)
		{
			cp.Style = (cp.Style | addStyle) & ~removeStyle;
			cp.ExStyle = (cp.ExStyle | addExStyle) & ~removeExStyle;
			return cp;
		}
	}

	/// <summary>
	/// Holds the controls of a Gui which has a menu bar; see <see cref="KeysharpForm.ContentContainer"/>.
	/// </summary>
	internal sealed class KeysharpContentPanel : Panel
	{
		protected override CreateParams CreateParams => base.CreateParams.Unclipped(WindowsAPI.WS_CLIPCHILDREN);

		protected override void WndProc(ref Message m)
		{
			if (!this.TryForwardContextMenu(ref m))
				base.WndProc(ref m);
		}
	}

	//A tab page represents the child dialog AutoHotkey uses for a Tab3.
	internal sealed class KeysharpTabPage(string text) : TabPage(text)
	{
		protected override CreateParams CreateParams => base.CreateParams.Unclipped(WindowsAPI.WS_CLIPCHILDREN);
	}

	//The radio-group container must not clip its controls or intercept clicks between its buttons.
	internal sealed class KeysharpRadioPanel : Panel
	{
		protected override CreateParams CreateParams => base.CreateParams.Unclipped(WindowsAPI.WS_CLIPSIBLINGS | WindowsAPI.WS_CLIPCHILDREN);

		protected override void WndProc(ref Message m)
		{
			if (m.Msg == WindowsAPI.WM_NCHITTEST)
				m.Result = WindowsAPI.HTTRANSPARENT;
			else
				base.WndProc(ref m);
		}
	}

	public class KeysharpButton : Button
	{
		private readonly int addStyle, removeStyle;
		private readonly int addExStyle, removeExStyle;

		protected override CreateParams CreateParams => base.CreateParams.Unclipped().WithStyles(addStyle, removeStyle, addExStyle, removeExStyle);

		public KeysharpButton(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
			addStyle = _addStyle;
			addExStyle = _addExStyle;
			removeStyle = _removeStyle;
			removeExStyle = _removeExStyle;

			//Any setting of this field causes click events to behave unpredictably, so don't use it, and therefor don't support double clicks.
			//This differs from the documentation which says that double click is supported.
			//This isn't worth implementing since it's behavior that will almost never be desired.
			//SetStyle(ControlStyles.StandardClick | ControlStyles.StandardDoubleClick, true);
		}

		protected override void WndProc(ref Message m)
		{
			if (!GuiHelper.CallMessageHandler(this, ref m))
				base.WndProc(ref m);
		}
	}

	public class KeysharpCheckBox : CheckBox
	{
		private readonly int addStyle, removeStyle;
		private readonly int addExStyle, removeExStyle;

		protected override CreateParams CreateParams => base.CreateParams.Unclipped().WithStyles(addStyle, removeStyle, addExStyle, removeExStyle);

		protected override bool ShowFocusCues => false;

		public KeysharpCheckBox(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
			addStyle = _addStyle;
			addExStyle = _addExStyle;
			removeStyle = _removeStyle;
			removeExStyle = _removeExStyle;

			if ((addStyle & WindowsAPI.BS_NOTIFY) == WindowsAPI.BS_NOTIFY)
				SetStyle(ControlStyles.StandardClick | ControlStyles.StandardDoubleClick, true);
		}

		protected override void OnMouseUp(MouseEventArgs e)
		{
			if ((addStyle & WindowsAPI.BS_NOTIFY) == WindowsAPI.BS_NOTIFY)
				ResetFlagsandPaint();
			else
				base.OnMouseUp(e);
		}

		protected override void WndProc(ref Message m)
		{
			if (!GuiHelper.CallMessageHandler(this, ref m))
				base.WndProc(ref m);
		}
	}

	public class KeysharpComboBox : ComboBox
	{
		private readonly int addStyle, removeStyle;
		private readonly int addExStyle, removeExStyle;

		protected override CreateParams CreateParams => base.CreateParams.Unclipped(WindowsAPI.WS_CLIPSIBLINGS | WindowsAPI.WS_CLIPCHILDREN).WithStyles(addStyle, removeStyle, addExStyle, removeExStyle);

		public KeysharpComboBox(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
			addStyle = _addStyle;
			addExStyle = _addExStyle;
			removeStyle = _removeStyle;
			removeExStyle = _removeExStyle;
		}

		protected override void WndProc(ref Message m)
		{
			if (!GuiHelper.CallMessageHandler(this, ref m))
				base.WndProc(ref m);
		}
	}

	public class KeysharpDateTimePicker : DateTimePicker
	{
		private readonly int addStyle, removeStyle;
		private readonly int addExStyle, removeExStyle;

		protected override CreateParams CreateParams => base.CreateParams.Unclipped().WithStyles(addStyle, removeStyle, addExStyle, removeExStyle);

		public KeysharpDateTimePicker(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
			addStyle = _addStyle;
			addExStyle = _addExStyle;
			removeStyle = _removeStyle;
			removeExStyle = _removeExStyle;
		}

		protected override void WndProc(ref Message m)
		{
			if (!GuiHelper.CallMessageHandler(this, ref m))
				base.WndProc(ref m);
		}
	}

	public partial class KeysharpCustomControl : Control
	{
		private static readonly int ICC_ANIMATE_CLASS      = 0x00000080;
		private static readonly int ICC_BAR_CLASSES        = 0x00000004;
		private static readonly int ICC_COOL_CLASSES       = 0x00000400;
		private static readonly int ICC_DATE_CLASSES       = 0x00000100;
		private static readonly int ICC_HOTKEY_CLASS       = 0x00000040;
		private static readonly int ICC_INTERNET_CLASSES   = 0x00000800;
		private static readonly int ICC_LINK_CLASS         = 0x00008000;
		private static readonly int ICC_LISTVIEW_CLASSES   = 0x00000001;
		private static readonly int ICC_NATIVEFNTCTL_CLASS = 0x00002000;
		private static readonly int ICC_PAGESCROLLER_CLASS = 0x00001000;
		private static readonly int ICC_PROGRESS_CLASS     = 0x00000020;
		private static readonly int ICC_STANDARD_CLASSES   = 0x00004000;
		private static readonly int ICC_TAB_CLASSES        = 0x00000008;
		private static readonly int ICC_TREEVIEW_CLASSES   = 0x00000002;
		private static readonly int ICC_UPDOWN_CLASS       = 0x00000010;
		private static readonly int ICC_USEREX_CLASSES     = 0x00000200;
		private static readonly int ICC_WIN95_CLASSES      = 0x000000FF;
		private readonly int addExStyle, removeExStyle;
		private readonly int addStyle, removeStyle;
		private readonly string className;

		protected override CreateParams CreateParams
		{
			get
			{
				var cp = base.CreateParams;
				cp.ClassName = className;
				return cp.Unclipped().WithStyles(addStyle, removeStyle, addExStyle, removeExStyle);
			}
		}

		static KeysharpCustomControl()
		{
			var icce = new INITCOMMONCONTROLSEX
			{
				dwSize = (uint)Marshal.SizeOf<INITCOMMONCONTROLSEX>(),
				dwICC  = (uint)(ICC_LISTVIEW_CLASSES
				| ICC_TREEVIEW_CLASSES
				| ICC_BAR_CLASSES
				| ICC_TAB_CLASSES
				| ICC_UPDOWN_CLASS
				| ICC_PROGRESS_CLASS
				| ICC_HOTKEY_CLASS
				| ICC_ANIMATE_CLASS
				| ICC_WIN95_CLASSES
				| ICC_DATE_CLASSES
				| ICC_USEREX_CLASSES
				| ICC_COOL_CLASSES
				| ICC_INTERNET_CLASSES
				| ICC_PAGESCROLLER_CLASS
				| ICC_NATIVEFNTCTL_CLASS
				| ICC_STANDARD_CLASSES
				| ICC_LINK_CLASS)
			};

			if (!WindowsAPI.InitCommonControlsEx(in icce))
				throw new Win32Exception(Marshal.GetLastWin32Error());
		}

		public KeysharpCustomControl(string _className, int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
			className = _className;
			addStyle = _addStyle;
			addExStyle = _addExStyle;
			removeStyle = _removeStyle;
			removeExStyle = _removeExStyle;
		}

		protected override void WndProc(ref Message m)
		{
			if (!GuiHelper.CallMessageHandler(this, ref m))
				base.WndProc(ref m);
		}

		protected override void OnHandleCreated(EventArgs e)
		{
			base.OnHandleCreated(e);
			// Defer the relayout until after creation/layout finishes so the control has its final size.
			BeginInvoke(RelayoutNativeChildren);
		}

		protected override void OnSizeChanged(EventArgs e)
		{
			base.OnSizeChanged(e);
			RelayoutNativeChildren();
		}

		// Some common controls (notably SysIPAddress32) lay out their internal child fields in response to
		// WM_SETFONT rather than WM_SIZE. The order in which WinForms applies the font and the real bounds
		// during creation can leave those children sized to a zero height, so the control renders blank even
		// though it accepts input. Re-sending the current font forces the control to re-lay-out its children
		// to the current size. Re-sending the existing font handle (rather than a fresh one) avoids any GDI
		// font lifetime concerns, and is a no-op for custom classes that don't host child controls.
		private void RelayoutNativeChildren()
		{
			if (!IsHandleCreated)
				return;

			var hFont = WindowsAPI.SendMessage(Handle, (uint)WindowsAPI.WM_GETFONT, (nint)0, (nint)0);
			_ = WindowsAPI.SendMessage(Handle, (uint)WindowsAPI.WM_SETFONT, hFont, (nint)1);
		}

	}

	public class KeysharpTextBox : TextBox
	{
		private readonly int addStyle, removeStyle;
		private readonly int addExStyle, removeExStyle;

		protected override CreateParams CreateParams => base.CreateParams.Unclipped().WithStyles(addStyle, removeStyle, addExStyle, removeExStyle);

		public KeysharpTextBox(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
			addStyle = _addStyle;
			addExStyle = _addExStyle;
			removeStyle = _removeStyle;
			removeExStyle = _removeExStyle;

		}

		protected override void WndProc(ref Message m)
		{
			if (!GuiHelper.CallMessageHandler(this, ref m))
				base.WndProc(ref m);
		}
	}

	public class KeysharpPasswordBox : KeysharpTextBox
	{
		public KeysharpPasswordBox(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
			: base(_addStyle, _addExStyle, _removeStyle, _removeExStyle)
		{
		}
	}

	public class KeysharpGroupBox : GroupBox
	{
		private readonly int addStyle, removeStyle;
		private readonly int addExStyle, removeExStyle;

		protected override CreateParams CreateParams =>
			base.CreateParams.Unclipped(WindowsAPI.WS_CLIPSIBLINGS | WindowsAPI.WS_CLIPCHILDREN).WithStyles(addStyle, removeStyle, addExStyle, removeExStyle);

		public KeysharpGroupBox(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
			addStyle = _addStyle;
			addExStyle = _addExStyle;
			removeStyle = _removeStyle;
			removeExStyle = _removeExStyle;
		}

		protected override void OnPaintBackground(PaintEventArgs e)
		{
			//A group holding its controls as children paints its whole client area, which no other control shares.
			if (HasChildren)
			{
				base.OnPaintBackground(e);
				return;
			}

			//An explicit background color belongs to the caption; the interior follows the parent.
			if (BackColor.A == 255 && !string.IsNullOrEmpty(Text))
			{
				const int indent = 8;
				var width = TextRenderer.MeasureText(e.Graphics, Text, Font).Width;
				using var brush = new SolidBrush(BackColor);
				e.Graphics.FillRectangle(brush, RightToLeft == RightToLeft.Yes ? Width - indent - width : indent, 0, width, Font.Height);
			}
		}

		public override string Text
		{
			get => base.Text;
			set
			{
				//GroupBox restores WM_SETREDRAW only after TextChanged has run.
				base.Text = value;
				if (!HasChildren && Parent is Control parent)
					parent.Invalidate(new Rectangle(Left, Top, Width, Font.Height), true);
			}
		}

		protected override void WndProc(ref Message m)
		{
			if (!GuiHelper.CallMessageHandler(this, ref m))
				base.WndProc(ref m);
		}
	}

	public class KeysharpLabel : Label
	{
		private int addStyle, removeStyle;
		private readonly int addExStyle, removeExStyle;

		protected override CreateParams CreateParams => base.CreateParams.Unclipped().WithStyles(addStyle, removeStyle, addExStyle, removeExStyle);

		/// <summary>Adds SS_NOTIFY for good, as AutoHotkey does once the script handles a Click or DoubleClick.</summary>
		internal void EnableNotify()
		{
			addStyle |= WindowsAPI.SS_NOTIFY;
			removeStyle &= ~WindowsAPI.SS_NOTIFY;

			if (IsHandleCreated)
				_ = WindowsAPI.SetWindowLongPtr(Handle, WindowsAPI.GWL_STYLE, WindowsAPI.GetWindowLongPtr(Handle, WindowsAPI.GWL_STYLE) | WindowsAPI.SS_NOTIFY);
		}

		/// <summary>
		/// Stops the label fitting its caption and records its present size, which caption changes would otherwise
		/// replace with the last one set through WinForms.
		/// </summary>
		internal void FixSize()
		{
			var bounds = Bounds;
			AutoSize = false;
			MinimumSize = MaximumSize = Size.Empty;
			SetBoundsCore(bounds.X, bounds.Y, bounds.Width, bounds.Height, BoundsSpecified.Size);
		}

		public KeysharpLabel(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
			addStyle = _addStyle;
			//BackgroundTrans must expose siblings, including native progress bars.
			//WinForms transparency only composites the parent background, so use backgroundless
			//WS_EX_TRANSPARENT painting and erase old captions through the parent.
			addExStyle = _addExStyle;
			removeStyle = _removeStyle;
			removeExStyle = _removeExStyle;

			if ((_addExStyle & 0x20) == 0x20)
			{
				SetStyle(ControlStyles.Opaque, true);
				SetStyle(ControlStyles.OptimizedDoubleBuffer, false);
			}
		}

		/// <summary>
		/// Paints the parent first when the label is backgroundless, since only the parent erases the previous
		/// caption; painting the label alone draws the new caption over it.
		/// </summary>
		public override void Refresh()
		{
			if (GetStyle(ControlStyles.Opaque) && Parent is Control p)
			{
				p.Invalidate(Bounds, true);
				p.Update();
				Update();
			}
			else
				base.Refresh();
		}

		protected override void WndProc(ref Message m)
		{
			if (GuiHelper.CallMessageHandler(this, ref m))
				return;

			base.WndProc(ref m);

			//Repainted here rather than in OnTextChanged, which an attached UpDown writing its position does not
			//raise. A backgroundless label needs the parent and siblings repainted to erase its previous caption.
			if (m.Msg == WindowsAPI.WM_SETTEXT)
			{
				if (GetStyle(ControlStyles.Opaque) && Parent is Control p)
					p.Invalidate(Bounds, true);

				Invalidate();
			}
		}
	}

	public class KeysharpLinkLabel : LinkLabel
	{
		internal bool clickSet = false;
		private readonly int addStyle, removeStyle;
		private readonly int addExStyle, removeExStyle;

		protected override CreateParams CreateParams => base.CreateParams.Unclipped().WithStyles(addStyle, removeStyle, addExStyle, removeExStyle);

		public KeysharpLinkLabel(string text, int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
			addStyle = _addStyle;
			addExStyle = _addExStyle;
			removeStyle = _removeStyle;
			removeExStyle = _removeExStyle;

			if (text != "")
			{
				var links = GuiHelper.ParseLinkLabelText(text);
				Text = links.Item1;
				Links.Clear();

				foreach (var link in links.Item2)
					_ = Links.Add(link.Item1, link.Item2, link.Item3);
			}
		}

		internal static object OnLinkLabelClicked(object obj0, object obj1)
		{
			if (obj0 is LinkLabel ll && obj1 is LinkLabelLinkClickedEventArgs e)
			{
				var url = e.Link.LinkData is Tuple<string, string> tss ? tss.Item2.ToString() : ll.Text.Substring(e.Link.Start, e.Link.Length);

				if (!url.Contains("://"))
					url = "https://" + url;

				_ = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
				ll.Links[ll.Links.IndexOf(e.Link)].Visited = true;
			}

			return null;
		}

		protected override void WndProc(ref Message m)
		{
			if (!GuiHelper.CallMessageHandler(this, ref m))
				base.WndProc(ref m);
		}
	}

	public class KeysharpListBox : ListBox
	{
		private readonly int addStyle, removeStyle;
		private readonly int addExStyle, removeExStyle;

		protected override CreateParams CreateParams => base.CreateParams.Unclipped().WithStyles(addStyle, removeStyle, addExStyle, removeExStyle);

		public KeysharpListBox(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
			addStyle = _addStyle;
			addExStyle = _addExStyle;
			removeStyle = _removeStyle;
			removeExStyle = _removeExStyle;
		}

		protected override void WndProc(ref Message m)
		{
			if (!GuiHelper.CallMessageHandler(this, ref m))
				base.WndProc(ref m);
		}
	}

	public class KeysharpListView : ListView
	{
		private readonly int addStyle, removeStyle;
		private readonly int addExStyle, removeExStyle;
		private readonly nint[] nativeImageLists = new nint[3];

		internal int SmallImageCount => SmallImageList?.Images.Count ?? WindowsAPI.ImageList_GetImageCount(nativeImageLists[1]);

		protected override CreateParams CreateParams => base.CreateParams.Unclipped(WindowsAPI.WS_CLIPSIBLINGS | WindowsAPI.WS_CLIPCHILDREN).WithStyles(addStyle, removeStyle, addExStyle, removeExStyle);

		public KeysharpListView(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
			addStyle = _addStyle;
			addExStyle = _addExStyle;
			removeStyle = _removeStyle;
			removeExStyle = _removeExStyle;
			ColumnClick += KeysharpListView_ColumnClick;
		}

		internal long ReplaceImageList(ImageList list, nint handle, int type)
		{
			var managed = type switch
			{
				0 => LargeImageList,
				1 => SmallImageList,
				_ => StateImageList
			};

			var old = WindowsAPI.SendMessage(Handle, WindowsAPI.LVM_SETIMAGELIST, (nint)type, 0);
			var oldId = managed != null && managed.Handle == old ? ImageLists.IL_GetId(managed) : old.ToInt64();
			nativeImageLists[type] = 0;

			switch (type)
			{
				case 0:
					LargeImageList = list;
					break;

				case 1:
					SmallImageList = list;
					break;

				default:
					StateImageList = list;
					break;
			}

			nativeImageLists[type] = list == null ? handle : 0;
			_ = WindowsAPI.SendMessage(Handle, WindowsAPI.LVM_SETIMAGELIST, (nint)type, handle);
			return oldId;
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				ImageLists.DestroyWithListView(addStyle, LargeImageList, SmallImageList, StateImageList);
				SyncNativeImageLists(false);

				if ((addStyle & 0x40) == 0)
					foreach (var handle in nativeImageLists.Distinct())
						if (handle != 0)
							_ = WindowsAPI.ImageList_Destroy(handle);

				System.Array.Clear(nativeImageLists);
			}

			base.Dispose(disposing);
		}

		protected override void OnHandleCreated(EventArgs e)
		{
			base.OnHandleCreated(e);
			SyncNativeImageLists(true);
		}

		protected override void DestroyHandle()
		{
			// Native checkbox state lists can be destroyed with the handle even when LVS_SHAREIMAGELISTS is set.
			SyncNativeImageLists(false);
			base.DestroyHandle();
		}

		internal void RefreshColors(int row = -1)
		{
			if (this.GetGuiControl() is not Gui.ListView owner)
				return;

			for (var i = row < 0 ? 0 : row; i < (row < 0 ? Items.Count : row + 1); i++)
			{
				var item = Items[i];
				var rowColors = owner.GetColors(i + 1, 0);
				item.UseItemStyleForSubItems = !owner.HasColors;
				item.ForeColor = rowColors.Text ?? Color.Empty;
				item.BackColor = rowColors.Back ?? Color.Empty;

				for (var j = 0; j < item.SubItems.Count; j++)
				{
					var cellColors = owner.GetColors(i + 1, j + 1);
					// Subitem zero shares the item's style, so every other cell needs its own inherited colors.
					item.SubItems[j].ForeColor = cellColors.Text ?? (owner.HasColors ? ForeColor : Color.Empty);
					item.SubItems[j].BackColor = cellColors.Back ?? (owner.HasColors ? BackColor : Color.Empty);
				}
			}

			Invalidate();
		}

		internal void SetListViewColumnSizes(int width = -2)
		{
			foreach (ColumnHeader col in Columns)
				col.Width = width;
		}

		protected override void OnForeColorChanged(EventArgs e)
		{
			base.OnForeColorChanged(e);
			RefreshColors();
		}

		protected override void OnBackColorChanged(EventArgs e)
		{
			base.OnBackColorChanged(e);
			RefreshColors();
		}

		protected override void WndProc(ref Message m)
		{
			if (!GuiHelper.CallMessageHandler(this, ref m))
				base.WndProc(ref m);
		}

		//Subscribed ahead of the holder's ColClick, so the rows are sorted by the time the script hears of the click.
		private void KeysharpListView_ColumnClick(object sender, ColumnClickEventArgs e) => (this.GetGuiControl() as Gui.ListView)?.SortByHeader(e.Column);

		private void SyncNativeImageLists(bool attach)
		{
			if (IsHandleCreated)
				for (var type = 0; type < nativeImageLists.Length; type++)
					if (nativeImageLists[type] != 0)
						_ = WindowsAPI.SendMessage(Handle, WindowsAPI.LVM_SETIMAGELIST, (nint)type, attach ? nativeImageLists[type] : 0);
		}
	}

	public class KeysharpMonthCalendar : MonthCalendar
	{
		private readonly int addStyle, removeStyle;
		private readonly int addExStyle, removeExStyle;

		protected override CreateParams CreateParams => base.CreateParams.Unclipped().WithStyles(addStyle, removeStyle, addExStyle, removeExStyle);

		public KeysharpMonthCalendar(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
			addStyle = _addStyle;
			addExStyle = _addExStyle;
			removeStyle = _removeStyle;
			removeExStyle = _removeExStyle;
		}

		protected override void WndProc(ref Message m)
		{
			if (!GuiHelper.CallMessageHandler(this, ref m))
				base.WndProc(ref m);
		}
	}

	public class KeysharpNumericUpDown : NumericUpDown
	{
		private readonly int addStyle, removeStyle;
		private readonly int addExStyle, removeExStyle;

		protected override CreateParams CreateParams => base.CreateParams.Unclipped(WindowsAPI.WS_CLIPSIBLINGS | WindowsAPI.WS_CLIPCHILDREN).WithStyles(addStyle, removeStyle, addExStyle, removeExStyle);

		public KeysharpNumericUpDown(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
			addStyle = _addStyle;
			addExStyle = _addExStyle;
			removeStyle = _removeStyle;
			removeExStyle = _removeExStyle;
			//We tried getting wrap to work but it's impossible within Winforms.
		}

		protected override void WndProc(ref Message m)
		{
			if (!GuiHelper.CallMessageHandler(this, ref m))
				base.WndProc(ref m);
		}
	}

	/// <summary>
	/// The native up-down control an UpDown becomes when it attaches to an Edit or Text control. With
	/// UDS_SETBUDDYINT its position is the number in the buddy, so the two always agree.
	/// </summary>
	public class KeysharpUpDown : Control
	{
		private const int UDS_SETBUDDYINT = 0x02, UDS_ALIGNRIGHT = 0x04, UDS_AUTOBUDDY = 0x10, UDS_ARROWKEYS = 0x20;
		private const uint UDM_SETBUDDY = 0x0400 + 105, UDM_SETACCEL = 0x0400 + 107, UDM_SETBASE = 0x0400 + 109;
		private const uint UDM_SETRANGE32 = 0x0400 + 111, UDM_SETPOS32 = 0x0400 + 113, UDM_GETPOS32 = 0x0400 + 114;
		private readonly int addStyle, removeStyle;
		private readonly int addExStyle, removeExStyle;
		private bool attached, hexadecimal;
		private int minimum, maximum = 100, increment, position;

		internal Control Buddy { get; }

		public event EventHandler ValueChanged;

		protected override CreateParams CreateParams
		{
			get
			{
				var cp = base.CreateParams;
				cp.ClassName = "msctls_updown32";
				cp.Style |= UDS_SETBUDDYINT | UDS_ALIGNRIGHT | UDS_ARROWKEYS;
				_ = cp.Unclipped().WithStyles(addStyle, removeStyle, addExStyle, removeExStyle);
				//The buddy is linked by message, since UDS_AUTOBUDDY takes whichever window precedes this one in the
				//z-order.
				cp.Style &= ~UDS_AUTOBUDDY;
				return cp;
			}
		}

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		internal bool Hexadecimal
		{
			get => hexadecimal;
			set
			{
				hexadecimal = value;

				if (attached)
					_ = Send(UDM_SETBASE, value ? 16 : 10, 0);
			}
		}

		/// <summary>
		/// The step of one arrow click, or 0 for the control's own accelerating default.
		/// </summary>
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		internal int Increment
		{
			get => increment;
			set
			{
				increment = value;

				if (attached)
					SetAccel();
			}
		}

		internal int Minimum => minimum;
		internal int Maximum => maximum;

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		internal int Value
		{
			get
			{
				if (attached && IsHandleCreated)
					position = (int)Send(UDM_GETPOS32, 0, 0);

				return position;
			}
			set
			{
				position = value;

				//The control keeps the position inside the range and writes it to the buddy.
				if (attached)
					_ = Send(UDM_SETPOS32, 0, value);
			}
		}

		static KeysharpUpDown()
		{
			var icce = new INITCOMMONCONTROLSEX
			{
				dwSize = (uint)Marshal.SizeOf<INITCOMMONCONTROLSEX>(),
				dwICC = 0x10//ICC_UPDOWN_CLASS
			};
			_ = WindowsAPI.InitCommonControlsEx(in icce);
		}

		internal KeysharpUpDown(Control buddy, int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
			Buddy = buddy;
			addStyle = _addStyle;
			addExStyle = _addExStyle;
			removeStyle = _removeStyle;
			removeExStyle = _removeExStyle;
			TabStop = false;//The arrows act on the Edit, which keeps the focus.
			SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, false);//The control draws its own arrows.
		}

		/// <summary>
		/// Links the buddy and writes the starting position. Content-sized buddies keep their width beside the arrows.
		/// </summary>
		internal void Attach(bool keepBuddyWidth)
		{
			//Freeze the label before native sizing, then record the resulting bounds.
			var label = Buddy as KeysharpLabel;
			label?.FixSize();
			_ = Send(UDM_SETBUDDY, Buddy.Handle, 0);

			if (keepBuddyWidth)
			{
				if ((removeStyle & UDS_ALIGNRIGHT) == 0)
					Left += Width;

				Buddy.Width += Width;
			}

			label?.FixSize();
			attached = true;
			ApplyState();
			_ = Send(UDM_SETPOS32, 0, position);
			//Changing the Edit's casing or alignment re-creates its window.
			Buddy.HandleCreated += (_, _) => Relink();
		}

		internal void SetRange(int low, int high)
		{
			minimum = low;
			maximum = high;

			if (attached)
				_ = Send(UDM_SETRANGE32, low, high);
		}

		protected override void OnHandleCreated(EventArgs e)
		{
			base.OnHandleCreated(e);

			//The Edit's text kept the position while this window was re-created.
			if (attached)
			{
				Relink();
				ApplyState();
			}
		}

		protected override void WndProc(ref Message m)
		{
			if (GuiHelper.CallMessageHandler(this, ref m))
				return;

			//The parent reflects the scroll message the control sends it for each arrow click or arrow key. As in
			//AutoHotkey, only SB_THUMBPOSITION counts, since a mouse click is followed by SB_ENDSCROLL as well.
			if ((m.Msg == WindowsAPI.WM_REFLECT + WindowsAPI.WM_VSCROLL || m.Msg == WindowsAPI.WM_REFLECT + WindowsAPI.WM_HSCROLL)
					&& (m.WParam.ToInt64() & 0xFFFF) == WindowsAPI.SB_THUMBPOSITION)
			{
				ValueChanged?.Invoke(this, EventArgs.Empty);
				return;
			}

			base.WndProc(ref m);
		}

		/// <summary>
		/// Links a re-created window, of either control, to the other again. Both keep their bounds, since the
		/// alignment would otherwise narrow the Edit a second time.
		/// </summary>
		private void Relink()
		{
			if (!IsHandleCreated || !Buddy.IsHandleCreated)
				return;

			var bounds = Bounds;
			var buddyBounds = Buddy.Bounds;
			_ = Send(UDM_SETBUDDY, Buddy.Handle, 0);
			Buddy.Bounds = buddyBounds;
			Bounds = bounds;
		}

		private void ApplyState()
		{
			_ = Send(UDM_SETRANGE32, minimum, maximum);
			SetAccel();

			if (hexadecimal)
				_ = Send(UDM_SETBASE, 16, 0);
		}

		private unsafe void SetAccel()
		{
			if (increment <= 0)
				return;

			var accel = stackalloc uint[2] { 0, (uint)increment };//One UDACCEL: no delay, then this step.
			_ = Send(UDM_SETACCEL, 1, (nint)accel);
		}

		private nint Send(uint msg, nint wParam, nint lParam) => WindowsAPI.SendMessage(Handle, msg, wParam, lParam);
	}

	/// <summary>
	/// Takes the visual-style theme off a control, as AutoHotkey's -Theme does, so it draws in the classic style.
	/// Controls that WinForms paints itself (Button, CheckBox, Radio, GroupBox) keep their themed look.
	/// </summary>
	internal static partial class ControlTheme
	{
		[LibraryImport("uxtheme.dll", StringMarshalling = StringMarshalling.Utf16)]
		private static partial int SetWindowTheme(nint hwnd, string subAppName, string subIdList);

		internal static void Strip(Control control)
		{
			//A re-created window comes back themed.
			control.HandleCreated += (_, _) => StripWindow(control.Handle);

			if (control.IsHandleCreated)
				StripWindow(control.Handle);
		}

		/// <summary>Takes the theme off a window once, for a control which decides that each time its window is made.</summary>
		internal static void StripWindow(nint handle) => _ = SetWindowTheme(handle, "", "");
	}

	/// <summary>
	/// Derivation that allows setting the interpolation mode.
	/// Gotten from: https://www.codeproject.com/articles/717312/pixelbox-a-picturebox-with-configurable-interpolat
	/// </summary>
	public class KeysharpPictureBox : PictureBox
	{
		private int addStyle, removeStyle;
		private readonly int addExStyle, removeExStyle;
		private readonly InterpolationMode interpolationMode = InterpolationMode.NearestNeighbor;

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public string Filename { get; private set; }

		protected override CreateParams CreateParams => base.CreateParams.Unclipped().WithStyles(addStyle, removeStyle, addExStyle, removeExStyle);

		/// <summary>Adds SS_NOTIFY for good, as AutoHotkey does once the script handles a Click or DoubleClick.</summary>
		internal void EnableNotify()
		{
			addStyle |= WindowsAPI.SS_NOTIFY;
			removeStyle &= ~WindowsAPI.SS_NOTIFY;

			if (IsHandleCreated)
				_ = WindowsAPI.SetWindowLongPtr(Handle, WindowsAPI.GWL_STYLE, WindowsAPI.GetWindowLongPtr(Handle, WindowsAPI.GWL_STYLE) | WindowsAPI.SS_NOTIFY);
		}

		public KeysharpPictureBox(string filename, int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
			Filename = filename;
			addStyle = _addStyle;
			addExStyle = _addExStyle;
			removeStyle = _removeStyle;
			removeExStyle = _removeExStyle;

			if ((addExStyle & 0x20) == 0x20)
			{
				SetStyle(ControlStyles.Opaque, true);
				SetStyle(ControlStyles.OptimizedDoubleBuffer, false);
			}
		}

		protected override void OnPaint(PaintEventArgs pe)
		{
			pe.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
			pe.Graphics.InterpolationMode = interpolationMode;
			base.OnPaint(pe);
		}

		protected override void WndProc(ref Message m)
		{
			if (!GuiHelper.CallMessageHandler(this, ref m))
				base.WndProc(ref m);
		}
	}

	/// <summary>A native progress bar, themed until a color or Smooth requires the classic style.</summary>
	public class KeysharpProgressBar : ProgressBar
	{
		private const int CLR_DEFAULT = unchecked((int)0xFF000000), PBM_SETBARCOLOR = 0x0409;
		private readonly int addStyle, removeStyle, addExStyle, removeExStyle;
		private Color? barColor, backgroundColor;
		private bool classic, dark, changingTheme;

		internal int AddStyle => addStyle;

		protected override CreateParams CreateParams => base.CreateParams.Unclipped().WithStyles(addStyle, removeStyle, addExStyle, removeExStyle);

		public KeysharpProgressBar(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
			addStyle = _addStyle;
			addExStyle = _addExStyle;
			removeStyle = _removeStyle;
			removeExStyle = _removeExStyle;
		}

		internal void ApplyOptions(Color? color, Color? background)
		{
			if (color.HasValue)
				barColor = color.Value == DefaultForeColor ? null : color;
			if (background.HasValue)
				backgroundColor = background.Value == DefaultBackColor ? null : background;

			// AutoHotkey keeps the classic style after a color or Smooth option is reset.
			classic |= barColor.HasValue || backgroundColor.HasValue || Style == ProgressBarStyle.Continuous;
			if (IsHandleCreated)
			{
				StripTheme();
				RestoreColors();
			}
		}

		protected override void OnHandleCreated(EventArgs e)
		{
			dark = Application.IsDarkModeEnabled;
			classic |= Style == ProgressBarStyle.Continuous;
			// Strip before WinForms sets the position, avoiding a themed animation over overlapping controls.
			StripTheme();
			base.OnHandleCreated(e);
			// WinForms registers its own color reset in OnHandleCreated; restore our options after it.
			SystemEvents.UserPreferenceChanged += RestoreColors;
			RestoreColors();
		}

		protected override void OnHandleDestroyed(EventArgs e)
		{
			SystemEvents.UserPreferenceChanged -= RestoreColors;
			base.OnHandleDestroyed(e);
		}

		protected override void OnForeColorChanged(EventArgs e)
		{
			base.OnForeColorChanged(e);
			RestoreColors();
		}

		protected override void OnBackColorChanged(EventArgs e)
		{
			base.OnBackColorChanged(e);
			RestoreColors();
		}

		private void RestoreColors(object sender, UserPreferenceChangedEventArgs e) => RestoreColors();

		// Progress colors come from its own options, rather than the Gui's inherited text and background colors.
		private void RestoreColors()
		{
			if (!IsHandleCreated)
				return;

			var bar = barColor ?? (dark ? SystemColors.Highlight : (Color?)null);
			var back = backgroundColor ?? (dark ? SystemColors.Window : (Color?)null);
			_ = WindowsAPI.SendMessage(Handle, PBM_SETBARCOLOR, 0, bar is { } b ? ColorTranslator.ToWin32(b) : CLR_DEFAULT);
			_ = WindowsAPI.SendMessage(Handle, WindowsAPI.PBM_SETBKCOLOR, 0, back is { } k ? ColorTranslator.ToWin32(k) : CLR_DEFAULT);
		}

		private void StripTheme()
		{
			if ((!classic && !dark) || changingTheme)
				return;

			changingTheme = true;
			try { ControlTheme.StripWindow(Handle); }
			finally { changingTheme = false; }
		}

		protected override void WndProc(ref Message m)
		{
			if (GuiHelper.CallMessageHandler(this, ref m))
				return;

			base.WndProc(ref m);
			if (m.Msg == (int)WindowsAPI.WM_THEMECHANGED && !changingTheme)
			{
				StripTheme();
				RestoreColors();
			}
		}
	}

	public class KeysharpRadioButton : RadioButton
	{
		private readonly int addStyle, removeStyle;
		private readonly int addExStyle, removeExStyle;

		protected override CreateParams CreateParams => base.CreateParams.Unclipped().WithStyles(addStyle, removeStyle, addExStyle, removeExStyle);

		public KeysharpRadioButton(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
			addStyle = _addStyle;
			addExStyle = _addExStyle;
			removeStyle = _removeStyle;
			removeExStyle = _removeExStyle;

			if ((addStyle & WindowsAPI.BS_NOTIFY) == WindowsAPI.BS_NOTIFY)
				SetStyle(ControlStyles.StandardClick | ControlStyles.StandardDoubleClick, true);
		}

		protected override void WndProc(ref Message m)
		{
			if (!GuiHelper.CallMessageHandler(this, ref m))
				base.WndProc(ref m);
		}
	}

	public partial class KeysharpRichEdit : RichTextBox
	{
		private readonly int addStyle, removeStyle;
		private readonly int addExStyle, removeExStyle;

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		internal CharacterCasing CharacterCasing { get; set; } = CharacterCasing.Normal;

		// The rich edit control ignores ES_UPPERCASE and ES_LOWERCASE, so KeyPress converts typed characters and these
		// setters convert the text a script assigns.
		public override string Text
		{
			get => base.Text;
			set => base.Text = ApplyCasing(value);
		}

		public override string SelectedText
		{
			get => base.SelectedText;
			set => base.SelectedText = ApplyCasing(value);
		}

		private string ApplyCasing(string text) => CharacterCasing switch
		{
			CharacterCasing.Upper => text?.ToUpperInvariant(),
			CharacterCasing.Lower => text?.ToLowerInvariant(),
			_ => text
		};

		protected override CreateParams CreateParams => base.CreateParams.Unclipped().WithStyles(addStyle, removeStyle, addExStyle, removeExStyle);

		public KeysharpRichEdit(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
			addStyle = _addStyle;
			addExStyle = _addExStyle;
			removeStyle = _removeStyle;
			removeExStyle = _removeExStyle;
			KeyPress += KeysharpRichEdit_KeyPress;
		}

		protected override void WndProc(ref Message m)
		{
			if (!GuiHelper.CallMessageHandler(this, ref m))
				base.WndProc(ref m);
		}

		private void KeysharpRichEdit_KeyPress(object sender, KeyPressEventArgs e)
		{
			switch (CharacterCasing)
			{
				case CharacterCasing.Normal:
					break;

				case CharacterCasing.Upper:
					e.KeyChar = char.ToUpper(e.KeyChar);
					break;

				case CharacterCasing.Lower:
					e.KeyChar = char.ToLower(e.KeyChar);
					break;

				default:
					break;
			}
		}
	}

	public class KeysharpStatusStrip : StatusStrip
	{
		private readonly int addStyle, removeStyle;
		private readonly int addExStyle, removeExStyle;

		protected override CreateParams CreateParams => base.CreateParams.Unclipped().WithStyles(addStyle, removeStyle, addExStyle, removeExStyle);

		public KeysharpStatusStrip(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
			addStyle = _addStyle;
			addExStyle = _addExStyle;
			removeStyle = _removeStyle;
			removeExStyle = _removeExStyle;
		}

		protected override void WndProc(ref Message m)
		{
			if (GuiHelper.CallMessageHandler(this, ref m))
				return;

			if (m.Msg != 0x02E2) //WM_DPICHANGED_BEFOREPARENT
			{
				base.WndProc(ref m);
				return;
			}

			//WinForms can skip RescaleConstantsForDpi when the font already matches the new DPI.
			var oldDpi = DeviceDpi;
			var previous = ImageScalingSize;
			base.WndProc(ref m);
			if (oldDpi != DeviceDpi)
				ImageScalingSize = this.GetGuiControl()?.dpiResize == false ? previous : Gui.ScaleForDpi(previous, oldDpi, DeviceDpi);
		}
	}

	public class KeysharpTabControl : TabControl
	{
		internal Color? bgcolor;
		private readonly int addStyle, removeStyle;
		private readonly int addExStyle, removeExStyle;

		//Keep the tab's sibling clipping while allowing invalidation to reach its layout pages.
		protected override CreateParams CreateParams =>
			base.CreateParams.Unclipped(WindowsAPI.WS_CLIPCHILDREN).WithStyles(addStyle, removeStyle, addExStyle, removeExStyle);

		public KeysharpTabControl(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
			addStyle = _addStyle;
			addExStyle = _addExStyle;
			removeStyle = _removeStyle;
			removeExStyle = _removeExStyle;
			Click += KeysharpTabControl_Click;
			Enter += KeysharpTabControl_Enter;
			ControlAdded += KeysharpTabControl_ControlAdded;
			//SetStyle(ControlStyles.UserPaint, true);
			//SetStyle(ControlStyles.AllPaintingInWmPaint, true);
			//SetStyle(ControlStyles.DoubleBuffer, true);
			//SetStyle(ControlStyles.ResizeRedraw, true);
			//SetStyle(ControlStyles.SupportsTransparentBackColor, true);
		}

		internal void SetColor(Color color)
		{
			bgcolor = color;
			//((Control)this).BackColor = color;
			DrawMode = TabDrawMode.OwnerDrawFixed;

			foreach (TabPage tp in TabPages)
				tp.BackColor = color;
		}

		protected override void OnDrawItem(DrawItemEventArgs e)
		{
			if (bgcolor.HasValue)
			{
				using (var brush = new SolidBrush(bgcolor.Value))
				{
					var rect = DisplayRectangle;
					rect.Offset(1, 1);
					e.Graphics.FillRectangle(brush, rect);
					Rectangle paddedTabBounds = DisplayRectangle;

					for (var i = 0; i < TabPages.Count; i++)
					{
						var page = TabPages[i];
						paddedTabBounds = GetTabRect(i);
						paddedTabBounds.Offset(2, 2);
						paddedTabBounds.Width -= 4;
						paddedTabBounds.Height -= 2;
						Rectangle fillBounds = paddedTabBounds;

						if (this.SelectedIndex == i)
							fillBounds.Height += 4;

						e.Graphics.FillRectangle(brush, fillBounds);
						TextRenderer.DrawText(e.Graphics, page.Text, page.Font, paddedTabBounds, page.ForeColor);
					}

					Rectangle headerArea = new Rectangle(paddedTabBounds.Right + 2, 0, this.Width - paddedTabBounds.Right, this.DisplayRectangle.Top - 2);
					e.Graphics.FillRectangle(brush, headerArea);
				}
			}
			else
				base.OnDrawItem(e);
		}

		protected override void WndProc(ref Message m)
		{
			if (!GuiHelper.CallMessageHandler(this, ref m))
				base.WndProc(ref m);
		}

		/// <summary>
		/// Focusing gets rid of the unsightly dotted selection box on the tab.
		/// It may still show for a half second when donig custom drawing with bgcolor.
		/// </summary>
		private void KeysharpTabControl_Click(object sender, EventArgs e) => _ = SelectedTab?.Focus();

		private void KeysharpTabControl_Enter(object sender, EventArgs e) => _ = SelectedTab?.Focus();

		private void KeysharpTabControl_ControlAdded(object sender, ControlEventArgs e)
		{
			if (bgcolor.HasValue && e.Control is TabPage tp)
				tp.BackColor = bgcolor.Value;
		}
	}

	public class KeysharpToolStripStatusLabel : ToolStripStatusLabel
	{
		internal readonly CallbackRegistry doubleClickHandlers = new(CallbackStop.NonEmpty, "Gui");
		//No WndProc method to override because TSSL is not a Control.

		//The icon handle StatusBar.SetIcon returned, which this part owns as an AutoHotkey status bar part owns its HICON.
		private nint icon;

		public KeysharpToolStripStatusLabel(string text = "")
			: base(text)
		{
			DoubleClickEnabled = true;
			DoubleClick += KeysharpToolStripStatusLabel_DoubleClick;
		}

		/// <summary>
		/// Shows the bitmap as this part's icon, taking ownership of it, and returns an icon handle for it which stays
		/// valid until the icon is replaced or the part is disposed.
		/// </summary>
		internal nint SetIcon(Bitmap bitmap)
		{
			var old = Image;
			Image = bitmap;
			old?.Dispose();
			DestroyIcon();
			return icon = bitmap.GetHicon();
		}

		protected override void Dispose(bool disposing)
		{
			var image = Image;
			base.Dispose(disposing);

			if (disposing)
				image?.Dispose();

			DestroyIcon();
		}

		private void DestroyIcon()
		{
			if (icon != 0)
			{
				_ = WindowsAPI.DestroyIcon(icon);
				icon = 0;
			}
		}

		private void KeysharpToolStripStatusLabel_DoubleClick(object sender, EventArgs e) => doubleClickHandlers.InvokeEventHandlers(this, GetCurrentParent().Items.IndexOf(this) + 1);
	}

	public class KeysharpTrackBar : TrackBar
	{
		public bool inverted = false;
		private readonly int addStyle, removeStyle;
		private readonly int addExStyle, removeExStyle;

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public new int Value
		{
			get => inverted ? Maximum - base.Value + Minimum : base.Value;
			set => base.Value = inverted ? Maximum - value + Minimum : value;
		}

		protected override CreateParams CreateParams => base.CreateParams.Unclipped().WithStyles(addStyle, removeStyle, addExStyle, removeExStyle);

		public KeysharpTrackBar(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
			addStyle = _addStyle;
			addExStyle = _addExStyle;
			removeStyle = _removeStyle;
			removeExStyle = _removeExStyle;
		}

		protected override void WndProc(ref Message m)
		{
			if (!GuiHelper.CallMessageHandler(this, ref m))
				base.WndProc(ref m);
		}
	}

	public class KeysharpTreeView : TreeView
	{
		private readonly int addStyle, removeStyle;
		private readonly int addExStyle, removeExStyle;

		protected override CreateParams CreateParams => base.CreateParams.Unclipped().WithStyles(addStyle, removeStyle, addExStyle, removeExStyle);

		public KeysharpTreeView(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
			addStyle = _addStyle;
			addExStyle = _addExStyle;
			removeStyle = _removeStyle;
			removeExStyle = _removeExStyle;
		}

		protected override void WndProc(ref Message m)
		{
			if (!GuiHelper.CallMessageHandler(this, ref m))
				base.WndProc(ref m);
		}
	}
}
#endif
