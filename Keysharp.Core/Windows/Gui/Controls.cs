#if WINDOWS
using Label = System.Windows.Forms.Label;

namespace Keysharp.Core
{
	public class KeysharpButton : Button
	{
		private readonly int addStyle, removeStyle;
		private readonly int addExStyle, removeExStyle;

		protected override CreateParams CreateParams
		{
			get
			{
				var cp = base.CreateParams;
				cp.Style |= addStyle;
				cp.Style &= ~removeStyle;
				cp.ExStyle |= addExStyle;
				cp.ExStyle &= ~removeExStyle;
				return cp;
			}
		}

		public KeysharpButton(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
			addStyle = _addStyle;
			addExStyle = _addExStyle;
			removeStyle = _removeStyle;
			removeExStyle = _removeExStyle;
#if LINUX
			FlatStyle = FlatStyle.Flat;
#endif
			//Any setting of this field causes click events to behave unpredictably, so don't use it, and therefor don't support double clicks.
			//This differs from the documentation which says that double click is supported.
			//This isn't worth implementing since it's behavior that will almost never be desired.
			//SetStyle(ControlStyles.StandardClick | ControlStyles.StandardDoubleClick, true);
		}

#if WINDOWS

		protected override void WndProc(ref Message m)
		{
			if (!GuiHelper.CallMessageHandler(this, ref m))
				base.WndProc(ref m);
		}

#endif
	}

	public class KeysharpCheckBox : CheckBox
	{
		private readonly int addStyle, removeStyle;
		private readonly int addExStyle, removeExStyle;

		protected override CreateParams CreateParams
		{
			get
			{
				var cp = base.CreateParams;
				cp.Style |= addStyle;
				cp.Style &= ~removeStyle;
				cp.ExStyle |= addExStyle;
				cp.ExStyle &= ~removeExStyle;
				return cp;
			}
		}

		protected override bool ShowFocusCues => false;

		public KeysharpCheckBox(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
			addStyle = _addStyle;
			addExStyle = _addExStyle;
			removeStyle = _removeStyle;
			removeExStyle = _removeExStyle;
#if WINDOWS

			if ((addStyle & WindowsAPI.BS_NOTIFY) == WindowsAPI.BS_NOTIFY)
				SetStyle(ControlStyles.StandardClick | ControlStyles.StandardDoubleClick, true);

#else
			FlatStyle = FlatStyle.Flat;
#endif
		}

#if WINDOWS

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

#endif
	}

	public class KeysharpComboBox : ComboBox
	{
		private readonly int addStyle, removeStyle;
		private readonly int addExStyle, removeExStyle;

		protected override CreateParams CreateParams
		{
			get
			{
				var cp = base.CreateParams;
				cp.Style |= addStyle;
				cp.Style &= ~removeStyle;
				cp.ExStyle |= addExStyle;
				cp.ExStyle &= ~removeExStyle;
				return cp;
			}
		}

		public KeysharpComboBox(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
			addStyle = _addStyle;
			addExStyle = _addExStyle;
			removeStyle = _removeStyle;
			removeExStyle = _removeExStyle;
		}

#if WINDOWS

		protected override void WndProc(ref Message m)
		{
			if (!GuiHelper.CallMessageHandler(this, ref m))
				base.WndProc(ref m);
		}

#endif
	}

	public class KeysharpDateTimePicker : DateTimePicker
	{
		private readonly int addStyle, removeStyle;
		private readonly int addExStyle, removeExStyle;

		protected override CreateParams CreateParams
		{
			get
			{
				var cp = base.CreateParams;
				cp.Style |= addStyle;
				cp.Style &= ~removeStyle;
				cp.ExStyle |= addExStyle;
				cp.ExStyle &= ~removeExStyle;
				return cp;
			}
		}

		public KeysharpDateTimePicker(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
			addStyle = _addStyle;
			addExStyle = _addExStyle;
			removeStyle = _removeStyle;
			removeExStyle = _removeExStyle;
		}

#if WINDOWS

		protected override void WndProc(ref Message m)
		{
			if (!GuiHelper.CallMessageHandler(this, ref m))
				base.WndProc(ref m);
		}

#endif
	}

#if WINDOWS
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
				cp.Style |= addStyle;
				cp.Style &= ~removeStyle;
				cp.ExStyle |= addExStyle;
				cp.ExStyle &= ~removeExStyle;
				return cp;
			}
		}

		static KeysharpCustomControl()
		{
			var icce = new INITCOMMONCONTROLSEX
			{
				dwSize = Marshal.SizeOf<INITCOMMONCONTROLSEX>(),
				dwICC  = ICC_LISTVIEW_CLASSES
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
				| ICC_LINK_CLASS
			};

			if (!InitCommonControlsEx(ref icce))
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

		[LibraryImport("comctl32.dll", EntryPoint = "InitCommonControlsEx", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static partial bool InitCommonControlsEx(ref INITCOMMONCONTROLSEX icce);

		// comboex
		[StructLayout(LayoutKind.Sequential)]
		private struct INITCOMMONCONTROLSEX
		{
			public int dwSize;
			public int dwICC;
		}
	}
#endif

	public class KeysharpTextBox : TextBox
	{
		private readonly int addStyle, removeStyle;
		private readonly int addExStyle, removeExStyle;

#if !WINDOWS
		internal bool IsNumeric { get; set; }

#endif
		protected override CreateParams CreateParams
		{
			get
			{
				var cp = base.CreateParams;
				cp.Style |= addStyle;
				cp.Style &= ~removeStyle;
				cp.ExStyle |= addExStyle;
				cp.ExStyle &= ~removeExStyle;
				return cp;
			}
		}

		public KeysharpTextBox(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
			addStyle = _addStyle;
			addExStyle = _addExStyle;
			removeStyle = _removeStyle;
			removeExStyle = _removeExStyle;
#if !WINDOWS
			KeyPress += KeysharpEdit_KeyPress;
			Validating += KeysharpEdit_Validating;
#endif
		}

#if WINDOWS

		protected override void WndProc(ref Message m)
		{
			if (!GuiHelper.CallMessageHandler(this, ref m))
				base.WndProc(ref m);
		}

#else

		private void KeysharpEdit_Validating(object sender, System.ComponentModel.CancelEventArgs e)
		{
			if (IsNumeric)
			{
				if (Text.Any(ch => !char.IsDigit(ch)))
					Text = string.Join("", Text.Where(ch => char.IsDigit(ch)));
			}
		}

		private void KeysharpEdit_KeyPress(object sender, KeyPressEventArgs e)
		{
			if (IsNumeric)
			{
				if (ModifierKeys.HasFlag(Keys.Control))
				{
					var vch = '\u0016';

					if (e.KeyChar == vch)
					{
						var text = Clipboard.GetText();

						if (text.Any(ch => !char.IsDigit(ch)))
						{
							Text = string.Join("", Text.Where(ch => char.IsDigit(ch)));
							e.Handled = true;
						}
					}
				}

				if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar))
				{
					e.Handled = true;
				}
			}
		}
#endif
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

		protected override CreateParams CreateParams
		{
			get
			{
				var cp = base.CreateParams;
				cp.Style |= addStyle;
				cp.Style &= ~removeStyle;
				cp.ExStyle |= addExStyle;
				cp.ExStyle &= ~removeExStyle;
				return cp;
			}
		}

		public KeysharpGroupBox(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
			addStyle = _addStyle;
			addExStyle = _addExStyle;
			removeStyle = _removeStyle;
			removeExStyle = _removeExStyle;
		}

#if WINDOWS

		protected override void WndProc(ref Message m)
		{
			if (!GuiHelper.CallMessageHandler(this, ref m))
				base.WndProc(ref m);
		}

#endif
	}

	public class KeysharpLabel : Label
	{
		private readonly int addStyle, removeStyle;
		private readonly int addExStyle, removeExStyle;

		protected override CreateParams CreateParams
		{
			get
			{
				var cp = base.CreateParams;
				cp.Style |= addStyle;
				cp.Style &= ~removeStyle;
				cp.ExStyle |= addExStyle;
				cp.ExStyle &= ~removeExStyle;
				return cp;
			}
		}

		public KeysharpLabel(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
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

#if WINDOWS

        protected override void WndProc(ref Message m)
		{
			if (!GuiHelper.CallMessageHandler(this, ref m))
				base.WndProc(ref m);
		}

#endif
	}

	public class KeysharpLinkLabel : LinkLabel
	{
		internal bool clickSet = false;
		private readonly int addStyle, removeStyle;
		private readonly int addExStyle, removeExStyle;

		protected override CreateParams CreateParams
		{
			get
			{
				var cp = base.CreateParams;
				cp.Style |= addStyle;
				cp.Style &= ~removeStyle;
				cp.ExStyle |= addExStyle;
				cp.ExStyle &= ~removeExStyle;
				return cp;
			}
		}

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

#if WINDOWS
				_ = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
#else
				System.Diagnostics.Process proc = new System.Diagnostics.Process();
				proc.EnableRaisingEvents = false;
				proc.StartInfo.FileName = "xdg-open";
				proc.StartInfo.Arguments = url;
				proc.Start();
#endif
				ll.Links[ll.Links.IndexOf(e.Link)].Visited = true;
			}

			return null;
		}

#if WINDOWS

		protected override void WndProc(ref Message m)
		{
			if (!GuiHelper.CallMessageHandler(this, ref m))
				base.WndProc(ref m);
		}

#endif
	}

	public class KeysharpListBox : ListBox
	{
		private readonly int addStyle, removeStyle;
		private readonly int addExStyle, removeExStyle;

		protected override CreateParams CreateParams
		{
			get
			{
				var cp = base.CreateParams;
				cp.Style |= addStyle;
				cp.Style &= ~removeStyle;
				cp.ExStyle |= addExStyle;
				cp.ExStyle &= ~removeExStyle;
				return cp;
			}
		}

		public KeysharpListBox(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
			addStyle = _addStyle;
			addExStyle = _addExStyle;
			removeStyle = _removeStyle;
			removeExStyle = _removeExStyle;
		}

#if WINDOWS

		protected override void WndProc(ref Message m)
		{
			if (!GuiHelper.CallMessageHandler(this, ref m))
				base.WndProc(ref m);
		}

#endif
	}

	public class KeysharpListView : ListView
	{
		public bool uni = false;
		private int sortColumn = -1;
		private readonly int addStyle, removeStyle;
		private readonly int addExStyle, removeExStyle;

		protected override CreateParams CreateParams
		{
			get
			{
				var cp = base.CreateParams;
				cp.Style |= addStyle;
				cp.Style &= ~removeStyle;
				cp.ExStyle |= addExStyle;
				cp.ExStyle &= ~removeExStyle;
				return cp;
			}
		}

		public KeysharpListView(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
			addStyle = _addStyle;
			addExStyle = _addExStyle;
			removeStyle = _removeStyle;
			removeExStyle = _removeExStyle;
			ColumnClick += KeysharpListView_ColumnClick;
		}

#if LINUX
		//Linux has a bug where it will not draw the headers if the control is not initially shown.
		//Eg: a ListView on a tab that is not selected.
		//We had to make a new public method ListView.RedrawDetails() to expose this functionality
		//so we could call it from here.
		protected override void OnVisibleChanged(EventArgs e)
		{
			RedrawDetails();
			base.OnVisibleChanged(e);
		}
#endif

#if WINDOWS

		protected override void WndProc(ref Message m)
		{
			if (!GuiHelper.CallMessageHandler(this, ref m))
				base.WndProc(ref m);
		}

#endif

		/// <summary>
		/// Gotten from https://docs.microsoft.com/en-us/previous-versions/dotnet/articles/ms996467(v=msdn.10)
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void KeysharpListView_ColumnClick(object sender, ColumnClickEventArgs e)
		{
			if (Sorting == SortOrder.None)
				return;

			if (e.Column != sortColumn)//Determine whether the column is the same as the last column clicked.
			{
				sortColumn = e.Column;//Set the sort column to the new column.
				Sorting = Sorting == SortOrder.Ascending ? SortOrder.Descending : SortOrder.Ascending;//Determine what the last sort order was and change it.
			}
			else if (!uni)
				Sorting = Sorting == SortOrder.Ascending ? SortOrder.Descending : SortOrder.Ascending;//Determine what the last sort order was and change it.
		}
	}

	public class KeysharpMonthCalendar : MonthCalendar
	{
		private readonly int addStyle, removeStyle;
		private readonly int addExStyle, removeExStyle;

		protected override CreateParams CreateParams
		{
			get
			{
				var cp = base.CreateParams;
				cp.Style |= addStyle;
				cp.Style &= ~removeStyle;
				cp.ExStyle |= addExStyle;
				cp.ExStyle &= ~removeExStyle;
				return cp;
			}
		}

		public KeysharpMonthCalendar(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
			addStyle = _addStyle;
			addExStyle = _addExStyle;
			removeStyle = _removeStyle;
			removeExStyle = _removeExStyle;
		}

#if WINDOWS

		protected override void WndProc(ref Message m)
		{
			if (!GuiHelper.CallMessageHandler(this, ref m))
				base.WndProc(ref m);
		}

#endif
	}

	public class KeysharpNumericUpDown : NumericUpDown
	{
		private readonly int addStyle, removeStyle;
		private readonly int addExStyle, removeExStyle;

		protected override CreateParams CreateParams
		{
			get
			{
				var cp = base.CreateParams;
				cp.Style |= addStyle;
				cp.Style &= ~removeStyle;
				cp.ExStyle |= addExStyle;
				cp.ExStyle &= ~removeExStyle;
				return cp;
			}
		}

		public KeysharpNumericUpDown(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
			addStyle = _addStyle;
			addExStyle = _addExStyle;
			removeStyle = _removeStyle;
			removeExStyle = _removeExStyle;
			//We tried getting wrap to work but it's impossible within Winforms.
		}
#if WINDOWS

		protected override void WndProc(ref Message m)
		{
			if (!GuiHelper.CallMessageHandler(this, ref m))
				base.WndProc(ref m);
		}

#endif
	}

	/// <summary>
	/// Derivation that allows setting the interpolation mode.
	/// Gotten from: https://www.codeproject.com/articles/717312/pixelbox-a-picturebox-with-configurable-interpolat
	/// </summary>
	public class KeysharpPictureBox : PictureBox
	{
		private readonly int addStyle, removeStyle;
		private readonly int addExStyle, removeExStyle;
		private readonly InterpolationMode interpolationMode = InterpolationMode.NearestNeighbor;
		private bool scaleHeight;
		private bool scaleWidth;

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public string Filename { get; private set; }

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public bool ScaleHeight
		{
			get => scaleHeight;

			set
			{
				scaleHeight = value;

				if (scaleHeight)
					scaleWidth = false;
			}
		}

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public bool ScaleWidth
		{
			get => scaleWidth;

			set
			{
				scaleWidth = value;

				if (scaleWidth)
					scaleHeight = false;
			}
		}

		protected override CreateParams CreateParams
		{
			get
			{
				var cp = base.CreateParams;
				cp.Style |= addStyle;
				cp.Style &= ~removeStyle;
				cp.ExStyle |= addExStyle;
				cp.ExStyle &= ~removeExStyle;
				return cp;
			}
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

#if WINDOWS

		protected override void WndProc(ref Message m)
		{
			if (!GuiHelper.CallMessageHandler(this, ref m))
				base.WndProc(ref m);
		}

#endif
	}

	/// <summary>
	/// Derivation to allow for custom colors.
	/// Gotten from: https://stackoverflow.com/questions/778678/how-to-change-the-color-of-progressbar-in-c-sharp-net-3-5
	/// and: https://stackoverflow.com/questions/15795205/how-to-make-a-vertical-progress-bar
	/// </summary>
	public class KeysharpProgressBar : ProgressBar
	{
		private readonly bool customColors = false;
		private readonly int addStyle, removeStyle;
		private readonly int addExStyle, removeExStyle;

		internal int AddStyle => addStyle;

		protected override CreateParams CreateParams
		{
			get
			{
				var cp = base.CreateParams;
				cp.Style |= addStyle;
				cp.Style &= ~removeStyle;
				cp.ExStyle |= addExStyle;
				cp.ExStyle &= ~removeExStyle;
				return cp;
			}
		}

		public KeysharpProgressBar(bool _customColors, int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
			addStyle = _addStyle;
			addExStyle = _addExStyle;
			removeStyle = _removeStyle;
			removeExStyle = _removeExStyle;
			customColors = _customColors;
			SetStyle(ControlStyles.UserPaint, customColors);
			SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			if (customColors)
			{
				const int inset = 1;

				using (var offscreenImage = new Bitmap(Width, Height))
				{
					using (var offscreen = Graphics.FromImage(offscreenImage))
					{
						var rect = new Rectangle(0, 0, Width, Height);
						var scaleFactor = ((double)Value - Minimum) / ((double)Maximum - Minimum);
#if WINDOWS
						var vert = (AddStyle & 0x04) == 0x04;
#endif

						if (ProgressBarRenderer.IsSupported)
						{
#if WINDOWS

							if ((AddStyle & 0x04) == 0x04)
								ProgressBarRenderer.DrawVerticalBar(offscreen, rect);
							else
#endif
								ProgressBarRenderer.DrawHorizontalBar(offscreen, rect);
						}

						var bx = 0;
						var by = 0;
						var bw = 0;
						var bh = 0;
						var fy = inset;
#if WINDOWS

						if (vert)
						{
							rect.Width -= inset * 2;
							rect.Height = (int)((rect.Height - inset) * scaleFactor);
							fy = Height - rect.Height - inset;
							bx = inset;
							by = inset;
							bw = rect.Width;
							bh = fy;
						}
						else
#endif
						{
							rect.Width = (int)((rect.Width - inset) * scaleFactor);
							rect.Height -= inset * 2;
							bx = rect.Width + inset;
							by = inset;
							bw = Width - rect.Width - inset * 2;
							bh = rect.Height;
						}

						if (rect.Width < 0) rect.Width = 0;

						if (rect.Height < 0) rect.Height = 0;

						var brush = new SolidBrush(ForeColor);
						offscreen.FillRectangle(brush, inset, fy, rect.Width, rect.Height);
						var bkgBrush = new SolidBrush(BackColor);
						offscreen.FillRectangle(bkgBrush, bx, by, bw, bh);
						e.Graphics.DrawImage(offscreenImage, 0, 0);
					}
				}
			}
			else
				base.OnPaint(e);
		}

		//protected override void OnPaintBackground(PaintEventArgs e)
		//{
		//  if (!customColors)
		//      base.OnPaintBackground(e);
		//}

#if WINDOWS

		protected override void WndProc(ref Message m)
		{
			if (!GuiHelper.CallMessageHandler(this, ref m))
				base.WndProc(ref m);
		}

#endif
	}

	public class KeysharpRadioButton : RadioButton
	{
		private readonly int addStyle, removeStyle;
		private readonly int addExStyle, removeExStyle;

		protected override CreateParams CreateParams
		{
			get
			{
				var cp = base.CreateParams;
				cp.Style |= addStyle;
				cp.Style &= ~removeStyle;
				cp.ExStyle |= addExStyle;
				cp.ExStyle &= ~removeExStyle;
				return cp;
			}
		}

		public KeysharpRadioButton(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
			addStyle = _addStyle;
			addExStyle = _addExStyle;
			removeStyle = _removeStyle;
			removeExStyle = _removeExStyle;
#if WINDOWS

			if ((addStyle & WindowsAPI.BS_NOTIFY) == WindowsAPI.BS_NOTIFY)
				SetStyle(ControlStyles.StandardClick | ControlStyles.StandardDoubleClick, true);

#endif
		}

#if WINDOWS

		protected override void WndProc(ref Message m)
		{
			if (!GuiHelper.CallMessageHandler(this, ref m))
				base.WndProc(ref m);
		}

#endif
	}

	public class KeysharpRichEdit : RichTextBox
	{
		private readonly int addStyle, removeStyle;
		private readonly int addExStyle, removeExStyle;

#if !WINDOWS
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		internal bool IsNumeric { get; set; }
#endif

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		internal CharacterCasing CharacterCasing { get; set; } = CharacterCasing.Normal;

		protected override CreateParams CreateParams
		{
			get
			{
				var cp = base.CreateParams;
				cp.Style |= addStyle;
				cp.Style &= ~removeStyle;
				cp.ExStyle |= addExStyle;
				cp.ExStyle &= ~removeExStyle;
				return cp;
			}
		}

		public KeysharpRichEdit(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
			addStyle = _addStyle;
			addExStyle = _addExStyle;
			removeStyle = _removeStyle;
			removeExStyle = _removeExStyle;
			KeyPress += KeysharpRichEdit_KeyPress;
#if !WINDOWS
			Validating += KeysharpRichEdit_Validating;
#endif
		}

#if WINDOWS

		protected override void WndProc(ref Message m)
		{
			if (!GuiHelper.CallMessageHandler(this, ref m))
				base.WndProc(ref m);
		}

#else

		private void KeysharpRichEdit_Validating(object sender, System.ComponentModel.CancelEventArgs e)
		{
			if (IsNumeric)
			{
				if (Text.Any(ch => !char.IsDigit(ch)))
					Text = string.Join("", Text.Where(ch => char.IsDigit(ch)));
			}
		}
#endif

		private void KeysharpRichEdit_KeyPress(object sender, KeyPressEventArgs e)
		{
#if !WINDOWS

			if (IsNumeric)
			{
				if (ModifierKeys.HasFlag(Keys.Control))
				{
					var vch = '\u0016';

					if (e.KeyChar == vch)
					{
						var text = Clipboard.GetText();

						if (text.Any(ch => !char.IsDigit(ch)))
							e.Handled = true;
					}
				}

				if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar))
				{
					e.Handled = true;
				}
			}
			else
#endif
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
	}

	public class KeysharpStatusStrip : StatusStrip
	{
		private readonly int addStyle, removeStyle;
		private readonly int addExStyle, removeExStyle;

		protected override CreateParams CreateParams
		{
			get
			{
				var cp = base.CreateParams;
				cp.Style |= addStyle;
				cp.Style &= ~removeStyle;
				cp.ExStyle |= addExStyle;
				cp.ExStyle &= ~removeExStyle;
				return cp;
			}
		}

		public KeysharpStatusStrip(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
			addStyle = _addStyle;
			addExStyle = _addExStyle;
			removeStyle = _removeStyle;
			removeExStyle = _removeExStyle;
		}

#if WINDOWS

		protected override void WndProc(ref Message m)
		{
			if (!GuiHelper.CallMessageHandler(this, ref m))
				base.WndProc(ref m);
		}

#endif
	}

	public class KeysharpTabControl : TabControl
	{
		internal Color? bgcolor;
		private readonly int addStyle, removeStyle;
		private readonly int addExStyle, removeExStyle;

		protected override CreateParams CreateParams
		{
			get
			{
				var cp = base.CreateParams;
				cp.Style |= addStyle;
				cp.Style &= ~removeStyle;
				cp.ExStyle |= addExStyle;
				cp.ExStyle &= ~removeExStyle;
				return cp;
			}
		}

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

		internal void AdjustSize(double dpiscale, Size requestedSize)
		{
			if (requestedSize.Width != int.MinValue && requestedSize.Height != int.MinValue)
			{
				Width = requestedSize.Width;
				Height = requestedSize.Height;
				return;
			}

			var tempw = 0.0;
			var temph = 0.0;

			foreach (TabPage tp in TabPages)
			{
				(Control right, Control bottom) rb = tp.RightBottomMost();

				if (rb.right != null)
					tempw = Math.Max(tempw, this.TabWidth() + rb.right.Right + (tp.Margin.Right + Margin.Right));

				if (rb.bottom != null)
					temph = Math.Max(temph, this.TabHeight() + rb.bottom.Bottom + (tp.Margin.Bottom + (Margin.Bottom * dpiscale)));
			}

			Width  = requestedSize.Width == int.MinValue ? (int)Math.Round(tempw) : requestedSize.Width;
			Height = requestedSize.Height == int.MinValue ? (int)Math.Round(temph) : requestedSize.Height;
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

#if WINDOWS

		protected override void WndProc(ref Message m)
		{
			if (!GuiHelper.CallMessageHandler(this, ref m))
				base.WndProc(ref m);
		}

#endif

		/// <summary>
		/// Focusing gets rid of the unsightly dotted selection box on the tab.
		/// It may still show for a half second when donig custom drawing with bgcolor.
		/// </summary>
		private void KeysharpTabControl_Click(object sender, EventArgs e) => _ = SelectedTab.Focus();

		private void KeysharpTabControl_ControlAdded(object sender, ControlEventArgs e)
		{
			if (bgcolor.HasValue && e.Control is TabPage tp)
				tp.BackColor = bgcolor.Value;
		}

		private void KeysharpTabControl_Enter(object sender, EventArgs e) => _ = SelectedTab.Focus();
	}

	public class KeysharpToolStripStatusLabel : ToolStripStatusLabel
	{
		internal readonly List<IFuncObj> doubleClickHandlers = [];
		//No WndProc method to override because TSSL is not a Control.

		public KeysharpToolStripStatusLabel(string text = "")
			: base(text)
		{
			DoubleClickEnabled = true;
			DoubleClick += KeysharpToolStripStatusLabel_DoubleClick;
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

		protected override CreateParams CreateParams
		{
			get
			{
				var cp = base.CreateParams;
				cp.Style |= addStyle;
				cp.Style &= ~removeStyle;
				cp.ExStyle |= addExStyle;
				cp.ExStyle &= ~removeExStyle;
				return cp;
			}
		}

		public KeysharpTrackBar(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
			addStyle = _addStyle;
			addExStyle = _addExStyle;
			removeStyle = _removeStyle;
			removeExStyle = _removeExStyle;
		}

#if WINDOWS

		protected override void WndProc(ref Message m)
		{
			if (!GuiHelper.CallMessageHandler(this, ref m))
				base.WndProc(ref m);
		}

#endif
	}

	public class KeysharpTreeView : TreeView
	{
		private readonly int addStyle, removeStyle;
		private readonly int addExStyle, removeExStyle;
		private readonly Dictionary<TreeNode, bool> expandStates = [];

		protected override CreateParams CreateParams
		{
			get
			{
				var cp = base.CreateParams;
				cp.Style |= addStyle;
				cp.Style &= ~removeStyle;
				cp.ExStyle |= addExStyle;
				cp.ExStyle &= ~removeExStyle;
				return cp;
			}
		}

		public KeysharpTreeView(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
			addStyle = _addStyle;
			addExStyle = _addExStyle;
			removeStyle = _removeStyle;
			removeExStyle = _removeExStyle;
		}

		internal void DelayedExpandParent(TreeNode node)
		{
			var parent = node.Parent ?? node;

			if (expandStates.TryGetValue(parent, out var b))
				if (b)
					parent.Expand();
		}

		internal void MarkForExpansion(TreeNode node) => expandStates[node] = true;

		internal void RemoveMarkForExpansion(TreeNode node) => _ = expandStates.Remove(node);

#if WINDOWS

		protected override void WndProc(ref Message m)
		{
			if (!GuiHelper.CallMessageHandler(this, ref m))
				base.WndProc(ref m);
		}

#endif
	}

	public class KeysharpWebBrowser : WebBrowser
	{
		private readonly int addStyle, removeStyle;
		private readonly int addExStyle, removeExStyle;

		protected override CreateParams CreateParams
		{
			get
			{
				var cp = base.CreateParams;
				cp.Style |= addStyle;
				cp.Style &= ~removeStyle;
				cp.ExStyle |= addExStyle;
				cp.ExStyle &= ~removeExStyle;
				return cp;
			}
		}

		public KeysharpWebBrowser(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
			addStyle = _addStyle;
			addExStyle = _addExStyle;
			removeStyle = _removeStyle;
			removeExStyle = _removeExStyle;
		}

#if WINDOWS

		protected override void WndProc(ref Message m)
		{
			if (!GuiHelper.CallMessageHandler(this, ref m))
				base.WndProc(ref m);
		}

#endif
	}
}
#endif
