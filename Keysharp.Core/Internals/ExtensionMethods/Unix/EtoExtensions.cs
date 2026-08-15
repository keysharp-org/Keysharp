using Keysharp.Builtins;
#if !WINDOWS
using System.Reflection;
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
#if OSX
                            // WindowStyle.Utility (NSWindowStyleMask 0x10) is unsupported on
                            // modern macOS and logs a warning. A regular window with
                            // ShowInTaskbar = false is the closest equivalent.
                            form.WindowStyle = WindowStyle.Default;
#else
                            form.WindowStyle = WindowStyle.Utility;
#endif
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
                var key = handle.ToInt64();
                var allGuis = TheScript.GuiData.allGuiHwnds;
                if (allGuis.TryGetValue(key, out var gui))
                    return gui.form;
                // A GUI that has not run its field initializers yet has no control map at all, and every
                // registered GUI is walked here, built or not.
                foreach (var g in allGuis)
                {
                    if (g.Value?.controls is { } controls && controls.TryGetValue(key, out var ctrl))
                        return ctrl.GetControl();
                }

                // A window is registered under the handle it had when it was created, and on X11 that changes
                // the first time it is realized: the toolkit answers with a widget pointer until there is a
                // native window id. So a lookup by the CURRENT handle can miss a GUI still registered under the
                // earlier one; re-homing it here costs this scan once. A handle belonging to nothing of ours
                // pays it on every call, which is what keeps this last, after both cheap lookups.
                foreach (var g in allGuis)
                {
                    var form = g.Value?.form;

                    // Asking a form for its handle only works once it has a handler behind it: a disposed or
                    // not-yet-built window throws rather than answering, and neither can be the one being sought.
                    if (form == null || form.IsDisposed || form.Handler == null || form.Handle.ToInt64() != key)
                        continue;

                    _ = allGuis.TryRemove(g.Key, out _);
                    allGuis[key] = g.Value;
                    return form;
                }

                return null;
            }
		}

        extension(Eto.Forms.KeyEventArgs args)
        {
            internal Eto.Forms.Keys KeyCode => args.Key;
        }

#if LINUX
        // P/Invoke for X11 window id
        [DllImport("libgdk-3.so.0")]
        private static extern IntPtr gdk_x11_window_get_xid(IntPtr window);

        // Click-through uses GDK's backend-agnostic input-shape API together with a real cairo region
        // (the known-good hudkit-wayland recipe). gdk_window_input_shape_combine_region routes itself to the
        // X11 SHAPE input region or to the wl_surface input region depending on the active GDK backend, so a
        // single path works on both X11 and Wayland — and it avoids GtkSharp's Cairo.Region wrapper, which
        // did not produce a working empty region for us.
        [DllImport("libcairo.so.2")]
        private static extern IntPtr cairo_region_create();
        [DllImport("libcairo.so.2")]
        private static extern void cairo_region_destroy(IntPtr region);
        [DllImport("libgdk-3.so.0")]
        private static extern void gdk_window_input_shape_combine_region(IntPtr window, IntPtr shapeRegion, int offsetX, int offsetY);

        // Sets the xdg-toplevel app_id of a window on the Wayland backend. Re-sends to the live toplevel
        // when called after the window is mapped (GTK keeps impl->application_id and forwards it), so the
        // compositor re-resolves the matching desktop file.
        [DllImport("libgdk-3.so.0")]
        private static extern void gdk_wayland_window_set_application_id(IntPtr window, [MarshalAs(UnmanagedType.LPUTF8Str)] string appId);

        // Makes a toplevel override-redirect (X11): the window manager ignores it entirely, so it is placed and
        // sized exactly as asked (no gravity/keep-on-screen nudging when a live HUD resizes every frame) AND it
        // sits in the top stacking layer, above every managed window — including _NET_WM_STATE_ABOVE / Eto
        // +AlwaysOnTop ones. Must be set on a realized-but-unmapped GdkWindow so it takes effect at map time.
        [DllImport("libgdk-3.so.0")]
        private static extern void gdk_window_set_override_redirect(IntPtr window, bool overrideRedirect);
#endif

        // On Wayland a compositor (e.g. KWin) derives a window's titlebar/taskbar icon from its xdg-toplevel
        // app_id, which it matches to an installed "<app_id>.desktop" file and renders that entry's themed
        // Icon=. GTK3 exposes no per-window icon protocol on Wayland, so the pixbuf we set via Window.Icon is
        // ignored there (that path only feeds X11's _NET_WM_ICON). GTK's default app_id is the entry-assembly
        // name ("Keyview"/"Keysharp"), which does not match the lower-case installed desktop files, so the
        // compositor shows a generic icon. Overriding the app_id to the desktop-file base name restores the
        // logo. No-op on X11, where the pixbuf icon already works.
        // Returns true if the app_id was applied; false if it couldn't be (not Wayland, or the GdkWindow
        // isn't realized yet — on Wayland that is common at Shown, so the caller should retry once the map
        // has settled, e.g. via AsyncInvoke).
        internal static bool SetWaylandAppId(Form form, string appId)
        {
            if (form == null || string.IsNullOrEmpty(appId) || !Keysharp.Internals.Platform.Desktop.IsWaylandSession)
                return false;

#if LINUX
            try
            {
                if (form.ToNative() is Gtk.Window gtkWin && gtkWin.Window is Gdk.Window gdkWin)
                {
                    gdk_wayland_window_set_application_id(gdkWin.Handle, appId);
                    return true;
                }
            }
            catch
            {
            }
#endif
            return false;
        }

        // Makes a window transparent to mouse input (clicks pass through to whatever is beneath it).
        // Eto has no cross-platform option for this, so the native window is reached per backend:
        //   - macOS:     NSWindow.IgnoresMouseEvents
        //   - Linux/GTK: an empty GDK input-shape region (passing null restores normal input handling)
        // Called by KeysharpForm.SetClickThrough, and reapplied from the form's Shown handler because the
        // GTK input shape needs a realized GdkWindow. Unverified on Linux/macOS hosts.
        internal static void SetFormClickThrough(Form form, bool enable)
        {
            if (form == null)
                return;

            try
            {
#if OSX
                if (form.ControlObject is MonoMac.AppKit.NSWindow nsw)
                    Application.Instance.Invoke(() => nsw.IgnoresMouseEvents = enable);
#elif LINUX
                if (form.ToNative() is Gtk.Window gtkWin && gtkWin.Window is Gdk.Window gdkWin)
                {
                    if (enable)
                    {
                        // An empty input region means the window receives no pointer input, so clicks fall
                        // straight through to whatever is beneath it.
                        var region = cairo_region_create();
                        gdk_window_input_shape_combine_region(gdkWin.Handle, region, 0, 0);
                        cairo_region_destroy(region);
                    }
                    else
                    {
                        // A null region restores the default: the whole window receives input again.
                        gdk_window_input_shape_combine_region(gdkWin.Handle, IntPtr.Zero, 0, 0);
                    }

                    // On the Wayland backend GTK pushes the input region to the wl_surface only on the next
                    // frame (on X11 it applies immediately), so force a redraw to make it take effect.
                    gtkWin.QueueDraw();
                    KeepClickThrough(gtkWin, enable);
                }
#endif
            }
            catch
            {
            }
        }

#if LINUX
        // GTK recomputes a CSD window's input shape (the visible window plus its invisible shadow/resize margins)
        // inside gtk_window_size_allocate, which overwrites the empty region set above — and since GDK pushes the
        // region to the wl_surface only on the next frame, GTK's region is the one that actually reaches the
        // compositor and click-through is silently lost. "size-allocate" is G_SIGNAL_RUN_FIRST, so GTK's class
        // handler runs before this one, which makes reapplying here the last write of the frame — the one that
        // gets committed. Only CSD windows are affected, but the reapply is idempotent and cheap, so it is not
        // gated on the backend. `on` tracks the live -ClickThrough state so a disabled window is not silently
        // made click-through again by the next allocation.
        private static void KeepClickThrough(Gtk.Window gtkWin, bool enable)
        {
            if (clickThroughState.TryGetValue(gtkWin.Handle, out var on))
            {
                on.Value = enable;
                return;
            }

            if (!enable)
                return;

            clickThroughState[gtkWin.Handle] = on = new StrongBox<bool>(true);
            gtkWin.SizeAllocated += (o, a) =>
            {
                if (!on.Value || gtkWin.Window is not Gdk.Window w)
                    return;

                var region = cairo_region_create();
                gdk_window_input_shape_combine_region(w.Handle, region, 0, 0);
                cairo_region_destroy(region);
            };
            gtkWin.Destroyed += (o, e) => _ = clickThroughState.Remove(gtkWin.Handle);
        }

        // Click-through state per hooked window, so the size-allocate handler is attached only once (SetFormClickThrough
        // is called repeatedly: construction, Shown, and the post-map retry) and always sees the current setting.
        private static readonly Dictionary<IntPtr, StrongBox<bool>> clickThroughState = [];
#endif

        // Makes an image-overlay window override-redirect so it sits in the topmost X stacking layer, above EVERY
        // managed window — including _NET_WM_STATE_ABOVE / +AlwaysOnTop ones — so e.g. a highlight drawn over an
        // always-on-top window is actually visible. Earlier this used a DOCK type hint, but on Muffin DOCK shares
        // META_LAYER_TOP with ABOVE windows, so a focused always-on-top window still stacked over the overlay.
        // (Override-redirect also unmanages the window, but the overlay is already placed correctly via form.Location
        // without it, and the Drawable's 1:1 blit — not the WM — is what fixed the live-zoom scaling artifacts;
        // stacking above AlwaysOnTop is the reason this exists.)
        //
        // Override-redirect is an X11 concept, applied to a realized-but-unmapped GdkWindow so it lands before map
        // (hence the explicit Realize()). SKIPPED on Wayland: it is meaningless there and would mark the toplevel a
        // temp/override surface that may never get an xdg role and so never map — leaving the Eto fallback overlay
        // invisible (Wayland's real overlay path is the layer-shell backing anyway).
        internal static void SetFormOverlayTopmost(Form form)
        {
            if (form == null || Keysharp.Internals.Platform.Desktop.IsWaylandSession)
                return;

            try
            {
#if LINUX
                if (form.ToNative() is Gtk.Window gtkWin)
                {
                    if (gtkWin.Window == null)
                        gtkWin.Realize();   // create the (still unmapped) GdkWindow so the attribute lands before map

                    if (gtkWin.Window is Gdk.Window gdkWin)
                        gdk_window_set_override_redirect(gdkWin.Handle, true);
                }
#endif
            }
            catch
            {
            }
        }

        extension(Eto.Widget widget)
        {
            internal nint Handle {
                get
                {
#if LINUX
                    // X11 only: on Wayland the GdkWindow is not a GdkX11Window, so gdk_x11_window_get_xid
                    // asserts (a Gdk-CRITICAL per call) and returns 0 anyway. Skipping it on Wayland avoids
                    // that wasted native call + log spam — which, called per window operation, is a real cost
                    // when many overlay windows (e.g. OCR highlights) are created in a tight loop.
                    if (!Keysharp.Internals.Platform.Desktop.IsWaylandSession && widget is Form form)
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
#endif
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
                    var etoPref = control.GetPreferredSize();
                    return new Size(Convert.ToInt32(etoPref.Width), Convert.ToInt32(etoPref.Height));
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
			internal bool InvokeRequired => !TheScript.IsOnMainThread;
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
                    // Most controls expose a native Font property (via Eto's CommonControl), but containers
                    // such as Window/Form do not. For those, fall back to the Properties bag so a font set on
                    // the Gui (e.g. Gui.SetFont) is remembered and inherited by controls added afterwards.
                    var prop = control.GetType().GetProperty("Font");
                    if (prop != null && prop.PropertyType == typeof(Font) && prop.CanRead && prop.GetValue(control) is Font font)
                        return font;

                    if (control.Properties.TryGetValue("Font", out var stored) && stored is Font storedFont)
                        return storedFont;

                    return MainWindow.OurDefaultFont;
                }
                set
                {
                    var prop = control.GetType().GetProperty("Font");
			        if (prop != null && prop.PropertyType == typeof(Font) && prop.CanWrite)
				        prop.SetValue(control, value);
                    else
                        control.Properties["Font"] = value;
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

			/// <summary>
			/// Whether a point over this control should be answered with it. A TabControl lays every page out
			/// and leaves them all reporting Visible == true, so a hit test that only asked about visibility
			/// could return a control sitting on a page the user cannot see, let alone click.
			/// </summary>
			internal bool HitTestable => control.Visible
				&& (control is not TabPage page || page.Parent is not TabControl tabs || ReferenceEquals(tabs.SelectedPage, page));


            // Mirror WinForms Control.ClientRectangle: the client area expressed in CLIENT coordinates, so its
            // origin is always (0,0) (NOT the control's position within its container, which is what Bounds
            // gives). Consumers that need the client area's on-screen position must map it via PointToScreen
            // rather than reading it from here. Size matches Bounds.Size (Eto controls have no separate chrome).
            internal Rectangle ClientRectangle => new Rectangle(Point.Empty, control.Size);
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
                        KeysharpLinkLabel linkLabel => linkLabel.Text ?? "",
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
                        case KeysharpLinkLabel linkLabel:
                            linkLabel.Text = value;
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

    }
}
#endif
