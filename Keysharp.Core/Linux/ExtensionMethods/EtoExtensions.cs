#if !WINDOWS
using System.Xml.Linq;

namespace Eto.Forms 
{
    internal static class EtoExtensions
    {
        extension(Eto.Forms.Form)
        {
            internal static Form ActiveForm => Application.Instance.MainForm;
        }
        extension(Eto.Forms.Form form)
		{
			internal bool TopMost 
            {
                get => form.Topmost;
                set => form.Topmost = value;
            }
            internal bool MinimizeBox
            {
                get => form.Minimizable;
                set => form.Minimizable = value;
            }
            internal bool MaximizeBox
            {
                get => form.Maximizable;
                set => form.Maximizable = value;
            }
            internal Control ActiveControl => FindFocusedControl(form);
            internal MenuStrip MainMenuStrip
            {
                get => form.Properties.TryGetValue("MainMenuStrip", out var value) ? value as MenuStrip : null;
                set => form.Properties["MainMenuStrip"] = value;
            }
            internal FormBorderStyle FormBorderStyle
            {
                get
                {
                    if (form.Properties.TryGetValue("FormBorderStyle", out var value) && value is FormBorderStyle style)
                        return style;

                    return form.WindowStyle switch
                    {
                        WindowStyle.None => FormBorderStyle.None,
                        WindowStyle.Utility => FormBorderStyle.SizableToolWindow,
                        _ => FormBorderStyle.Sizable
                    };
                }
                set
                {
                    form.Properties["FormBorderStyle"] = value;
                    form.Resizable = value is FormBorderStyle.Sizable or FormBorderStyle.SizableToolWindow;
                    // Map common WinForms border styles to Eto window styles.
                    switch (value)
                    {
                        case FormBorderStyle.None:
                            form.WindowStyle = WindowStyle.None;
                            break;
                        case FormBorderStyle.FixedToolWindow:
                        case FormBorderStyle.SizableToolWindow:
                            form.WindowStyle = WindowStyle.Utility;
                            break;
                        default:
                            form.WindowStyle = WindowStyle.Default;
                            break;
                    }
                }
            }
            internal SizeGripStyle SizeGripStyle
            {
                get
                {
                    if (form.Properties.TryGetValue("SizeGripStyle", out var value) && value is SizeGripStyle style)
                        return style;

                    // Infer from resizable state if unset.
                    return form.Resizable ? SizeGripStyle.Auto : SizeGripStyle.Hide;
                }
                set
                {
                    form.Properties["SizeGripStyle"] = value;
                    // Eto doesn't expose a size grip; approximate by toggling resizability.
                    if (value == SizeGripStyle.Hide)
                        form.Resizable = false;
                    else if (value == SizeGripStyle.Auto || value == SizeGripStyle.Show)
                        form.Resizable = true;
                }
            }
            internal bool ControlBox
            {
                get => form.Properties.TryGetValue("ControlBox", out var value) && value is bool enabled && enabled;
                set => form.Properties["ControlBox"] = value;
            }
            internal FormStartPosition StartPosition
            {
                get => form.Properties.TryGetValue("StartPosition", out var value) && value is FormStartPosition position
                    ? position
                    : FormStartPosition.Manual;
                set => form.Properties["StartPosition"] = value;
            }

            internal void Hide() => form.Visible = false;
		}

        private static Control FindFocusedControl(Control root)
        {
            if (root == null)
                return null;

            if (root.HasFocus)
                return root;

            foreach (var child in root.VisualControls)
            {
                var focused = FindFocusedControl(child);
                if (focused != null)
                    return focused;
            }

            return null;
        }

		extension(Eto.Forms.Control)
		{
			internal static Color DefaultForeColor => SystemColors.ControlText;
            internal static Color DefaultBackColor => SystemColors.ControlBackground;
            internal static Control FromHandle(nint handle)
            {
                var allGuis = TheScript.GuiData.allGuiHwnds;
                if (allGuis.TryGetValue(handle, out var gui))
                    return gui.form;
                foreach (var g in TheScript.GuiData.allGuiHwnds)
                {
                    if (handle == g.Key)
                        return g.Value.form;
                    if (g.Value.controls.TryGetValue((long)handle, out var ctrl))
                        return ctrl.GetControl();
                }
                return null;
            }
		}

        extension(Eto.Forms.KeyEventArgs args)
        {
            internal Eto.Forms.Keys KeyCode => args.Key;
        }

        // P/Invoke for X11 window id
        [DllImport("libgdk-3.so.0")]
        private static extern IntPtr gdk_x11_window_get_xid(IntPtr window);

        extension(Eto.Widget widget)
        {
            internal nint Handle {
                get
                {
                    if (widget is Form form)
                    {
                        var native = form.ToNative() as Gtk.Window;
                        var gdkWin = native?.Window;
                        if (gdkWin != null)
                        {
                            var xid = gdk_x11_window_get_xid(gdkWin.Handle);
                            if (xid != 0)
                                return xid;
                        }
                    }

                    return widget.NativeHandle;
                }
            }
            internal string Name
            {
                get => widget.Properties.TryGetValue("Name", out object name) ? (string)name : "";
                set => widget.Properties["Name"] = value;
            }
        }

		extension(Eto.Forms.Control control)
		{
            internal Padding Margin
            {
                get => control.Properties.TryGetValue("Margin", out var margin) ? (Padding)margin : new Padding(0);
                set
                {
                    control.Properties["Margin"] = value;
                }
            }
            internal Color BackColor
            {
                get => control.BackgroundColor;
                set => control.BackgroundColor = value;
            }
            internal Color ForeColor
            {
                get
                {
                    if (control is TextControl tc)
                        return tc.TextColor;
                    if (control is ListControl lc)
                        return lc.TextColor;
                    if (control is DateTimePicker dtp)
                        return dtp.TextColor;
                    if (control is GroupBox gb)
                        return gb.TextColor;
                    if (control is NumericStepper ns)
                        return ns.TextColor;

                    var prop = control.GetType().GetProperty("TextColor");
                    if (prop != null && prop.PropertyType == typeof(Color) && prop.CanRead && prop.GetValue(control) is Color color)
                        return color;

                    if (control.Properties.TryGetValue("ForeColor", out var stored) && stored is Color storedColor)
                        return storedColor;

                    return SystemColors.ControlText;
                }
                set
                {
                    if (control is TextControl tc)
                        tc.TextColor = value;
                    else if (control is ListControl lc)
                        lc.TextColor = value;
                    else if (control is DateTimePicker dtp)
                        dtp.TextColor = value;
                    else if (control is GroupBox gb)
                        gb.TextColor = value;
                    else if (control is NumericStepper ns)
                        ns.TextColor = value;
                    else
                    {
                        var prop = control.GetType().GetProperty("TextColor");
                        if (prop != null && prop.PropertyType == typeof(Color) && prop.CanWrite)
                            prop.SetValue(control, value);
                    }

                    control.Properties["ForeColor"] = value;
                }
            }
            internal Size PreferredSize
            {
                get {
                    control.ToNative().GetPreferredSize(out var minSize, out var prefSize);
                    if (prefSize.Width <= 1 && prefSize.Height <= 1)
                    {
                        var prefSize2 = control.GetPreferredSize();
                        return new Size(prefSize2.Width.Ai(), prefSize2.Height.Ai());
                    }
                    return new Size(prefSize.Width.Ai(), prefSize.Height.Ai());
                }
            }
            internal Size ClientSize
            {
                get => control.ClientRectangle.Size;
                set {
                    if (control is Layout ll)
                        ll.ClientSize = value;
                    else
                        control.Size = value;
                }
            }
            internal Size MinimumSize
            {
                get => control.Size;
                set => _ = control.Size;
            }
            internal Size MaximumSize
            {
                get => control.Size;
                set => _ = control.Size;
            }
			internal bool IsHandleCreated => control.NativeHandle != 0;
			internal bool Disposing => false;
			internal bool InvokeRequired => CurrentThreadId() != TheScript.ProcessesData.MainThreadID;
            internal bool Focused => control.HasFocus;
            private static Point GetPLoc(Control c) => c.Parent is PixelLayout ? PixelLayout.GetLocation(c) : c.Location; 
            internal int Left
            {
                get => GetPLoc(control).X;
                set => PixelLayout.SetLocation(control, new Point(value, GetPLoc(control).Y));
            }

            internal int Top
            {
                get => GetPLoc(control).Y;
                set => PixelLayout.SetLocation(control, new Point(GetPLoc(control).X, value));
            }

            internal int Right
            {
                get => control.Left + control.GetSize().Width;
                set => control.SetSize(new Size(value - control.Left, control.GetSize().Height));
            }

            internal int Bottom
            {
                get => control.Top + control.GetSize().Height;
                set => control.SetSize(new Size(control.GetSize().Width, value - control.Top));
            }
            internal Font Font
            {
                get
                {
                    var prop = control.GetType().GetProperty("Font");
                    if (prop?.GetValue(control) is Font font)
                        return font;

                    return MainWindow.OurDefaultFont;
                }
                set
                {
                    var prop = control.GetType().GetProperty("Font");
			        if (prop != null && prop.PropertyType == typeof(Font) && prop.CanWrite)
				        prop.SetValue(control, value);
                }
            }
            internal DockStyle Dock
            {
                get => control.Properties.TryGetValue("Dock", out var value) && value is DockStyle dock
                    ? dock
                    : DockStyle.None;
                set => control.Properties["Dock"] = value;
            }
            internal void Activate() => control.Focus();
            internal void Refresh() => control.Invalidate();
			internal void Invoke(Action act) => Eto.Forms.Application.Instance.Invoke(act);
			internal T Invoke<T>(Func<T> act) => Eto.Forms.Application.Instance.Invoke<T>(act);
			internal IAsyncResult BeginInvoke(Action act) => Eto.Forms.Application.Instance.InvokeAsync(act);
			internal IEnumerable<Control> Controls => control is Container c ? c.Controls : control.VisualControls;


            internal Rectangle ClientRectangle => control.Bounds;
            internal Form FindForm() => (Form)control.ParentWindow;
            internal string Text
            {
                get
                {
                    return control switch
                    {
                        Window window => window.Title ?? "",
                        TextBox textBox => textBox.Text ?? "",
                        TextArea textArea => textArea.Text ?? "",
                        Label label => label.Text ?? "",
                        Button button => button.Text ?? "",
                        CheckBox checkBox => checkBox.Text ?? "",
                        RadioButton radioButton => radioButton.Text ?? "",
                        GroupBox groupBox => groupBox.Text ?? "",
                        ComboBox comboBox => comboBox.Text ?? "",
                        DropDown dropDown => dropDown.Text ?? "",
                        _ => ""
                    };
                }
                set
                {
                    value ??= "";
                    switch (control)
                    {
                        case Window window:
                            window.Title = value;
                            break;
                        case TextBox textBox:
                            textBox.Text = value;
                            break;
                        case TextArea textArea:
                            textArea.Text = value;
                            break;
                        case Label label:
                            label.Text = value;
                            break;
                        case Button button:
                            button.Text = value;
                            break;
                        case CheckBox checkBox:
                            checkBox.Text = value;
                            break;
                        case RadioButton radioButton:
                            radioButton.Text = value;
                            break;
                        case GroupBox groupBox:
                            groupBox.Text = value;
                            break;
                        case ComboBox comboBox:
                            comboBox.Text = value;
                            break;
                        case DropDown dropDown:
                            dropDown.Text = value;
                            break;
                    }
                }
            }
		}

        extension(Eto.Forms.Application)
        {
            internal static IEnumerable<Window> OpenForms => Application.Instance.Windows;
        }

		extension(Eto.Drawing.Color)
		{
            internal static Color Empty => SystemColors.Control;
            internal static Color Transparent => Colors.Transparent;
			internal static Color FromName(string name)
			{
				var color = Colors.Transparent;
				if (string.IsNullOrWhiteSpace(name))
					return color;

				var prop = typeof(Colors).GetProperty(
					name,
					BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase);

				if (prop == null || prop.PropertyType != typeof(Color))
					return color;

				return (Color)prop.GetValue(null);
			}
		}

        extension (Eto.Forms.TabControl tc)
        {
            internal TabPage SelectedTab 
            {
                get => tc.SelectedPage;
                set => tc.SelectedPage = value;
            }
        }

        extension (Eto.Forms.MenuItem item)
        {
            internal MenuItemCollection DropDownItems {
                get {
                    if (item is ISubmenu submenu)
                        return submenu.Items;

                    return null;
                }
            }
        }

        extension (Eto.Forms.TabControl tc)
		{
			internal Collection<TabPage> TabPages => tc.Pages;
		}

        extension (Eto.Forms.ComboBox comboBox)
        {
            internal int FindString(string value)
            {
                if (string.IsNullOrEmpty(value) || comboBox.DataStore == null)
                    return -1;

                var index = 0;
                foreach (var item in comboBox.DataStore)
                {
                    if (string.Equals(item?.ToString(), value, StringComparison.OrdinalIgnoreCase))
                        return index;
                    index++;
                }

                return -1;
            }

            internal bool DroppedDown
            {
                get => false;
                set => _ = value;
            }
        }

        extension (Eto.Forms.ListBox listBox)
        {
            internal int FindString(string value)
            {
                if (string.IsNullOrEmpty(value) || listBox.DataStore == null)
                    return -1;

                var index = 0;
                foreach (var item in listBox.DataStore)
                {
                    if (string.Equals(item?.ToString(), value, StringComparison.OrdinalIgnoreCase))
                        return index;
                    index++;
                }

                return -1;
            }
        }

        extension (Eto.Forms.TextBox textBox)
        {
            internal int SelectionStart
            {
                get
                {
                    var prop = textBox.GetType().GetProperty("CaretIndex");
                    return prop?.GetValue(textBox) is int i ? i : 0;
                }
                set
                {
                    var prop = textBox.GetType().GetProperty("CaretIndex");
                    prop?.SetValue(textBox, value);
                }
            }

            internal long GetLineFromCharIndex(int index) => 0;

            internal string[] Lines => (textBox.Text ?? "").Split('\n');

            internal void Paste(string text)
            {
                if (text == null)
                    return;

                textBox.Text = (textBox.Text ?? "") + text;
            }
        }

        extension (Eto.Forms.TextArea textArea)
        {
            internal int SelectionStart
            {
                get
                {
                    var prop = textArea.GetType().GetProperty("CaretIndex");
                    return prop?.GetValue(textArea) is int i ? i : 0;
                }
                set
                {
                    var prop = textArea.GetType().GetProperty("CaretIndex");
                    prop?.SetValue(textArea, value);
                }
            }

            internal long GetLineFromCharIndex(int index)
            {
                var text = textArea.Text ?? "";
                if (index <= 0)
                    return 0;

                var count = 0;
                for (var i = 0; i < Math.Min(index, text.Length); i++)
                {
                    if (text[i] == '\n')
                        count++;
                }

                return count;
            }

            internal string[] Lines => (textArea.Text ?? "").Split('\n');

            internal void Paste(string text)
            {
                if (text == null)
                    return;

                textArea.Text = (textArea.Text ?? "") + text;
            }
        }

		extension(Eto.Drawing.Color color)
		{
			internal bool IsKnownColor
			{
				get 
				{
					var props = typeof(Colors).GetProperties(BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase);
					foreach (var prop in props)
					{
						if (prop.PropertyType != typeof(Color))
							continue;
						if (prop.GetValue(null) is Color c && c == color)
							return true;
					}
					return false;
				}
			}
		}

        extension (Eto.Drawing.Graphics)
        {
            internal static Graphics FromImage(Bitmap bmp) => new Graphics(bmp);
        }

        extension (Eto.Drawing.Bitmap)
        {
            internal static Bitmap FromHbitmap(nint handle)
            {
                if (ImageHandleManager.TryGetImage(handle, out var image))
                {
                    if (image is Bitmap bmp)
                        return bmp.Clone();
                    if (image is Icon ico)
                        return ico.ToBitmap();
                }
                return null;
            }
        }

        extension (Eto.Drawing.Bitmap bmp)
        {
            internal void Save(string fileName)
            {
                bmp.Save(fileName, GetImageFormat(fileName));
            }
        }

        extension (Eto.Drawing.Icon ico)
        {
            internal Bitmap ToBitmap() => new Bitmap(ico);
        }
        extension (Eto.Drawing.Image img)
        {
            internal static Image FromFile(string fileName) => new Bitmap(fileName);
        }
        extension (Eto.Drawing.Font font)
        {
            internal float GetHeight(float dpi) => font.LineHeight;
        }
        internal static ImageFormat GetImageFormat(string fileName)
        {
            var ext = Path.GetExtension(fileName)?.ToLower();
            return ext switch
            {
                ".png" => ImageFormat.Png,
                ".jpg" or ".jpeg" => ImageFormat.Jpeg,
                ".bmp" => ImageFormat.Bitmap,
                ".gif" => ImageFormat.Gif,
                ".tif" or ".tiff" => ImageFormat.Tiff,
                _ => ImageFormat.Png // default
            };
        }

        extension (Eto.Forms.Clipboard)
        {
            internal static void SetImage(Image img)
            {
                Clipboard.Instance.Image = img;
            }
        }

    }
}
#endif
