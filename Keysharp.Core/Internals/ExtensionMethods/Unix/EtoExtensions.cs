using Keysharp.Builtins;
#if !WINDOWS
using System.Xml.Linq;

namespace Eto.Forms
{
    internal static class EtoExtensions
    {
        // Friend assemblies can call the getter without importing an internal extension property.
        internal static Color GetForeColor(Control control)
        {
            Color? color = control switch
            {
                TextControl tc => tc.TextColor,
                ListControl lc => lc.TextColor,
                DateTimePicker dtp => dtp.TextColor,
                GroupBox gb => gb.TextColor,
                NumericStepper ns => ns.TextColor,
                KeysharpLinkLabel link => link.TextColor,
                KeysharpListView listView => listView.TextColor,
                _ => null
            };

            return color ?? (control.Properties.TryGetValue("ForeColor", out var stored) && stored is Color storedColor
                ? storedColor : SystemColors.ControlText);
        }

        extension(Eto.Forms.Form)
        {
            internal static Form ActiveForm => Application.Instance.MainForm;
        }
        extension(Eto.Forms.Form form)
		{
            /// <summary>
            /// Runs <paramref name="onRealized"/> when the window acquires its native window id, which on X11
            /// is when <see cref="Handle"/> stops answering with a widget pointer and starts answering with the
            /// XID. Does nothing where the handle never changes.
            /// </summary>
            internal void OnRealized(Action onRealized)
            {
#if LINUX
                if (!Keysharp.Internals.Platform.Desktop.IsWaylandSession && form.ToNative() is Gtk.Widget native)
                    native.Realized += (o, e) => onRealized();
#endif
            }

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
        // <paramref name="appId"/> is the value the CALLER wants; window correlation temporarily needs its own
        // and wins for as long as it is matching, so the value actually written comes from there. Without that,
        // this call - deferred to an AsyncInvoke whenever the window is still unmapped, which is the normal case
        // at Shown - lands on top of a live correlation token and makes every first correlation time out.
        internal static bool SetWaylandAppId(Form form, string appId)
        {
            if (form == null || string.IsNullOrEmpty(appId) || !Keysharp.Internals.Platform.Desktop.IsWaylandSession)
                return false;

#if LINUX
            try
            {
                appId = Keysharp.Internals.Window.Linux.Wayland.WaylandOwnToplevels.CurrentAppId(form, appId);

                // GTK can use XWayland even in a Wayland session; this native call requires a Wayland window.
                if (form.ToNative() is Gtk.Window gtkWin && gtkWin.Window is Gdk.Window gdkWin
                    && GLib.GType.FromName("GdkWaylandWindow").IsInstance(gdkWin.Handle))
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

        internal static nint GetHandle(Eto.Widget widget)
        {
#if OSX
            if (widget is Window window)
            {
                if (window.Properties.TryGetValue("Keysharp.WindowId", out var cached))
                    return (nint)cached;

                return Application.Instance.Invoke(() =>
                {
                    // AppKit's number must identify this process's window in CoreGraphics.
                    var number = (nint)((MonoMac.AppKit.NSWindow)window.ControlObject).WindowNumber;
                    if (!Keysharp.Internals.Window.MacOS.MacNativeWindows.TryGetWindowInfo(number, out var info, false)
                        || info.OwnerPid != Environment.ProcessId)
                    {
                        _ = Errors.OSErrorOccurred("The macOS window has no matching window-server ID.");
                        return (nint)0;
                    }

                    window.Properties["Keysharp.WindowId"] = number;
                    return number;
                });
            }
#endif
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

        extension(Eto.Widget widget)
        {
            internal nint Handle => GetHandle(widget);
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
                get => GetForeColor(control);
                set
                {
                    switch (control)
                    {
                        case TextControl tc: tc.TextColor = value; break;
                        case ListControl lc: lc.TextColor = value; break;
                        case DateTimePicker dtp: dtp.TextColor = value; break;
                        case GroupBox gb: gb.TextColor = value; break;
                        case NumericStepper ns: ns.TextColor = value; break;
                        case KeysharpLinkLabel link: link.TextColor = value; break;
                        case KeysharpListView listView: listView.TextColor = value; break;
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
                // Containers such as a Form have no font of their own, so theirs is kept in the Properties bag, where
                // a font set on the Gui (e.g. Gui.SetFont) is remembered for the controls added afterwards.
                get => control switch
                {
                    CommonControl common => common.Font,
                    GroupBox group => group.Font,
                    KeysharpLinkLabel link => link.Font,
                    _ => null
                } ?? (control.Properties.TryGetValue("Font", out var stored) && stored is Font storedFont ? storedFont : MainWindow.OurDefaultFont);
                set
                {
                    switch (control)
                    {
                        case CommonControl common: common.Font = value; break;
                        case GroupBox group: group.Font = value; break;
                        case KeysharpLinkLabel link: link.Font = value; break;
                        default: control.Properties["Font"] = value; break;
                    }
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
                        TextControl textControl => textControl.Text ?? "",
                        KeysharpLinkLabel linkLabel => linkLabel.Text ?? "",
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
                        case TextControl textControl:
                            textControl.Text = value;
                            break;
                        case KeysharpLinkLabel linkLabel:
                            linkLabel.Text = value;
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
				return !string.IsNullOrWhiteSpace(name) && Color.TryParse(name, out var color)
					? color
					: Colors.Transparent;
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

        //As the Windows list messages search: from the first item, ignoring case, matching an item's start or all of it.
        extension (Eto.Forms.ListControl list)
        {
            internal int FindString(string value) => FindItem(list.DataStore, value, false);

            internal int FindStringExact(string value) => FindItem(list.DataStore, value, true);
        }

        private static int FindItem(IEnumerable<object> items, string value, bool exact)
        {
            if (string.IsNullOrEmpty(value) || items == null)
                return -1;

            var index = 0;

            foreach (var item in items)
            {
                var text = item?.ToString() ?? "";

                if (exact ? text.Equals(value, StringComparison.OrdinalIgnoreCase) : text.StartsWith(value, StringComparison.OrdinalIgnoreCase))
                    return index;

                index++;
            }

            return -1;
        }

        //GTK offsets count Unicode characters; .NET strings and the edit commands count UTF-16 units.
        internal static int TextOffset(TextControl control, int index, bool toNative)
        {
#if LINUX
            int characters = 0, units = 0;

            foreach (var rune in (control.Text ?? "").EnumerateRunes())
            {
                if ((toNative ? units : characters) >= index)
                    break;

                characters++;
                units += rune.Utf16SequenceLength;
            }

            return toNative ? characters : units;
#else
            return index;
#endif
        }

        //The caret and selection of an Edit or RichEdit, whichever Eto control it is. Eto gives a PasswordBox no caret, so on
        //Linux the GTK entry's own is used, and elsewhere it reads as the end of the text.
        extension (Eto.Forms.TextControl control)
        {
            internal int CaretIndex
            {
                get => control switch
                {
                    TextBox box => TextOffset(control, box.CaretIndex, false),
                    TextArea area => TextOffset(control, area.CaretIndex, false),
#if LINUX
                    PasswordBox when control.ControlObject is Gtk.Entry entry => TextOffset(control, entry.Position, false),
#endif
                    _ => (control.Text ?? "").Length
                };
                set
                {
                    switch (control)
                    {
                        case TextBox box: box.CaretIndex = TextOffset(control, value, true); break;
                        case TextArea area: area.CaretIndex = TextOffset(control, value, true); break;
#if LINUX
                        case PasswordBox when control.ControlObject is Gtk.Entry entry: entry.Position = TextOffset(control, value, true); break;
#endif
                    }
                }
            }

            internal Range<int> Selection
            {
                get
                {
                    Range<int> selection;

                    switch (control)
                    {
                        case TextBox box: selection = box.Selection; break;
                        case TextArea area: selection = area.Selection; break;
#if LINUX
                        case PasswordBox when control.ControlObject is Gtk.Entry entry:
                            _ = entry.GetSelectionBounds(out var start, out var end);
                            selection = start == end ? Range.FromLength(entry.Position, 0) : new Range<int>(Math.Min(start, end), Math.Max(start, end) - 1);
                            break;
#endif
                        default: return Range.FromLength(control.CaretIndex, 0);
                    }

                    return new Range<int>(TextOffset(control, selection.Start, false), TextOffset(control, selection.End + 1, false) - 1);
                }
                set
                {
                    var selection = new Range<int>(TextOffset(control, value.Start, true), TextOffset(control, value.End + 1, true) - 1);

                    switch (control)
                    {
                        case TextBox box: box.Selection = selection; break;
                        case TextArea area: area.Selection = selection; break;
#if LINUX
                        case PasswordBox when control.ControlObject is Gtk.Entry entry: entry.SelectRegion(selection.Start, selection.End + 1); break;
#endif
                    }
                }
            }

            /// <summary>
            /// Replaces the characters in <c>[start, start + length)</c>. A TextArea replaces only those, so a RichEdit
            /// keeps the formatting around them; the others take the whole text back. Where the caret ends up is up to
            /// the toolkit.
            /// </summary>
            internal void ReplaceText(int start, int length, string text)
            {
                if (control is TextArea area)
                {
                    control.Selection = Range.FromLength(start, length);
                    area.SelectedText = text;
                }
                else
                {
                    var current = control.Text ?? "";
                    control.Text = string.Concat(current.AsSpan(0, start), text, current.AsSpan(start + length));
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
