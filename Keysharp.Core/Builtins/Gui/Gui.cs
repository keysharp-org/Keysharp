using CallbackHub = Keysharp.Internals.Scripting.CallbackRegistry<Keysharp.Internals.Scripting.CallbackRegistration>;

namespace Keysharp.Builtins
{
	internal class GuiData
	{
		internal int windowCount = 0;
		internal ConcurrentDictionary<long, Gui> allGuiHwnds = new ();
		internal ConcurrentDictionary<int, WeakReference<Menu>> allMenus = new ();
		internal ConcurrentDictionary<long, MsgMonitor> onMessageHandlers = new ();

		/// <summary>
		/// A global counter of all menus in existence within the script.
		/// </summary>
		internal int menuCount = 0;
	}

	public partial class Gui : KeysharpObject, I__Enum, IEnumerable<(object, object)>
	{
		public TabPage CurrentTab;
		public KeysharpForm form;

		internal static Type[] GuiTypes =
			[
				typeof(Gui),
				typeof(Gui.Control),
				typeof(Menu),
				typeof(Control)//Add native control and form types just to be safe.
			];

		internal Dictionary<object, object> controls = [];
		// AHK's mDefaultDPIResize: the GUI-level "+/-DPIResize" only supplies the default for controls added
		// afterward, so toggling it never retroactively changes controls that already exist.
		internal bool defaultDpiResize = true;
#if WINDOWS
		internal bool dpiscaling = true;
#elif OSX
		internal bool dpiscaling = false;
#else
		internal bool dpiscaling = false;
#endif
#if WINDOWS
		// WinForms updates DeviceDpi when a PMv2 window crosses monitors. Never derive a GUI's layout from the
		// primary monitor: mixed-DPI windows must use the DPI of their own current native surface.
		internal double DpiScale => !dpiscaling ? 1.0 : Math.Max(0.01, (form?.DeviceDpi ?? 96) / 96.0);
#else
		// Eto/GTK and Cocoa expose their native logical widget units and manage backing scale themselves.
		internal double DpiScale => 1.0;
#endif
		internal MenuBar menuBar;
		bool marginsInit = false;
		internal nint owner = 0;
		internal Size requestedSize = new(int.MinValue, int.MinValue);
		internal Point requestedLocation = new(int.MinValue, int.MinValue);
		// "+MinSize"/"+MaxSize" with no dimensions pins the limit to the window's size; if the window has not
		// been shown yet, the size is captured at the first Gui.Show (see ApplyDeferredSizeLimits).
		private bool pinMinSizeToShowSize, pinMaxSizeToShowSize;
#if WINDOWS
		private static readonly Size NoSizeLimit = new(0, 0);//WinForms: (0,0) = no limit; negative values throw.
#else
		private static readonly Size NoSizeLimit = new(-1, -1);//Eto: (-1,-1) is the autosize / no-limit sentinel.
#endif

		private static readonly Dictionary<string, Action<Gui, object>> showOptionsDkt = new (StringComparer.OrdinalIgnoreCase)
		{
			{
				"AlwaysOnTop", (f, o) => {
					// AlwaysOnTop in Windows is handled with an ExStyle
#if !WINDOWS
					if (o is bool b) f.form.TopMost = b;
#endif
				}
			},
			{
				// macOS: controls whether the standard application menu bar (App/Edit/Window — the source of
				// Cmd+C/V/X/A/Z in text controls) is provided automatically. On by default; "-AppMenu" opts
				// out. No effect on Windows (no app-level menu); on Linux it suppresses Eto's system items.
				"AppMenu", (f, o) =>
				{
#if !WINDOWS
					if (o is bool b) f.includeAppMenu = b;
#endif
				}
			},
			{
				// Makes the window scroll when its contents are larger than its client area (e.g. a Picture
				// bigger than the window). On Windows this is the form's AutoScroll property; Eto has no
				// form-level equivalent, so the content layout is placed inside a Scrollable container.
				// Best applied at construction, before controls are added.
				"AutoScroll", (f, o) =>
				{
					if (o is bool b)
					{
#if WINDOWS
						f.form.AutoScroll = b;
#else
						// EnsureLayoutContainer drills through the Scrollable (a Panel) into the inner
						// PixelLayout, so controls still parent correctly once it is in place.
						if (b)
						{
							if (f.form.Content is not Scrollable)
								f.form.Content = new Scrollable { Content = f.form.Content ?? new PixelLayout(), Border = BorderType.None };
						}
						else if (f.form.Content is Scrollable sc)
						{
							f.form.Content = sc.Content;
						}
#endif
					}
				}
			},
			{
				"Border", (f, o) =>
				{
					// FormBorderStyle conflates caption presence with border thickness, and there's no
					// "border but no caption" value, so once the caption has been turned off via -Caption,
					// leave the borderless style alone rather than letting this silently bring it back.
					if (!f.caption)
						return;

					if (o is bool b && b)
						f.form.FormBorderStyle = f.resizable ? FormBorderStyle.Sizable : FormBorderStyle.FixedSingle;//No such thing as a resizable single pixel border.
					else
						f.form.FormBorderStyle = f.resizable ? FormBorderStyle.Sizable : FormBorderStyle.FixedDialog;
				}
			},
			{
				"Caption", (f, o) =>
				{
					if (o is bool b && b)
					{
						f.caption = true;
						f.form.FormBorderStyle = f.resizable ? FormBorderStyle.Sizable : FormBorderStyle.FixedDialog;
					}
					else
					{
						f.caption = false;
						f.form.FormBorderStyle = FormBorderStyle.None;
					}
				}
			},
			{
				// Makes the whole window transparent to mouse input — clicks pass through to whatever is
				// beneath it. On Windows this adds WS_EX_LAYERED|WS_EX_TRANSPARENT; on Linux it sets an
				// empty GTK input-shape region; on macOS it sets NSWindow.IgnoresMouseEvents. Pair with a
				// transparent background (e.g. WinSetTransColor) for a hollow, click-through overlay.
				"ClickThrough", (f, o) => { if (o is bool b) f.form.SetClickThrough(b); }
			},
			{
				"Disabled", (f, o) => { if (o is bool b) f.form.Enabled = !b; }
			},
			{
				// DPIScale is purely a unit conversion on the numbers the script supplies: +DPIScale multiplies
				// them by the window's DPI when a control is created and divides again in GetPos/GetClientPos,
				// while -DPIScale passes them through as raw pixels. It says nothing about what happens when the
				// DPI changes later — that is DPIResize, which is deliberately independent (as in AHK, where
				// RescaleForDPI ignores mUsesDPIScaling), so "-DPIScale +DPIResize" is a valid combination.
				"DPIScale", (f, o) => { if (o is bool b) f.dpiscaling = b; }
			},
			{
				// v2.1: whether controls are re-laid out when the window's DPI changes, e.g. after being dragged
				// to a monitor at a different scale. This sets the default for controls added afterward; an
				// individual control can override it with its own "+/-DPIResize" option.
				"DPIResize", (f, o) => { if (o is bool b) f.defaultDpiResize = b; }
			},
			{
				"LastFound", (f, o) =>
				{
					if (o is bool b)
					{
						f.lastfound = b;
						Script.TheScript.HwndLastUsed = f.Hwnd;
					}
				}
			},
			{
				"MaximizeBox", (f, o) => { if (o is bool b) f.form.MaximizeBox = b; }
			},
			{
				"MinimizeBox", (f, o) => { if (o is bool b) f.form.MinimizeBox = b; }
			},
			{
				"MinSize", (f, o) =>
				{
					if (o is ValueTuple<bool, string> t)
						f.SetSizeLimit(true, t.Item1, t.Item2);
				}
			},
			{
				"MaxSize", (f, o) =>
				{
					if (o is ValueTuple<bool, string> t)
						f.SetSizeLimit(false, t.Item1, t.Item2);
				}
			},
			{
				"OwnDialogs", (f, o) =>
				{
					if (o is bool b)
					{
						f.owndialogs = b;
						GuiHelper.DialogOwner = b ? f.form : null;
					}
				}
			},
			{
				"Owner", (f, o) =>
				{
					var s = o as string ?? "";
					nint ownerHandle = 0;

					if (int.TryParse(s, out var hwnd))
						ownerHandle = hwnd;
					else if (Script.TheScript.mainWindow is Form mw)   // +Owner with no HWND => owned by the script's main window (AHK).
						ownerHandle = mw.Handle;

					f.owner = ownerHandle;   // applied at Show time on Windows; set on the form directly elsewhere.
#if !WINDOWS
					if (Forms.Control.FromHandle(ownerHandle) is Form theform)
						f.form.Owner = theform;
#endif
					// An owned GUI has no taskbar button (matches AHK's +Owner behaviour).
					f.form.ShowInTaskbar = false;
				}
			},
#if WINDOWS
			{
				"Parent", (f, o) =>
				{
					if (o is string s)
					{
						if (int.TryParse(s, out var hwnd))
						{
							if (Forms.Control.FromHandle(new nint(hwnd)) is Form theform)
								f.form.Parent = theform;
						}
					}
				}
			},
#endif
			{
				"Resize", (f, o) =>
				{
					if (o is bool b)
					{
						f.resizable = b;
						f.form.MaximizeBox = b;

						if (f.caption)
							f.form.FormBorderStyle = b ? FormBorderStyle.Sizable : FormBorderStyle.FixedDialog;

						f.form.SizeGripStyle = b ? SizeGripStyle.Show : SizeGripStyle.Hide;
						if (b)
							f.form.AutoSize = false;
					}
				}
			},
			{
				// Rounds the window's outer corners, for a borderless window drawing its own chrome. Windows
				// 11 only; an accepted no-op elsewhere, see KeysharpForm.SetRoundedCorners.
				"Round", (f, o) => { if (o is bool b) f.form.SetRoundedCorners(b); }
			},
			{
				"SysMenu", (f, o) => { if (o is bool b) f.form.ControlBox = b; }
			},
			{
				"Theme", (f, o) => _ = Diagnostics.Debug.WriteLine("Themes are not supported", false)
			},
			{
				"ToolWindow", (f, o) => {
					if (o is bool b && b)
					{
						if (f.caption)//Only change border if they haven't requested that there be no caption/border.
							f.form.FormBorderStyle = f.resizable ? FormBorderStyle.SizableToolWindow : FormBorderStyle.FixedToolWindow;

						f.form.ShowInTaskbar = false;
					}
					else
					{
						if (f.caption)
							f.form.FormBorderStyle = f.resizable ? FormBorderStyle.Sizable : FormBorderStyle.FixedDialog;

						f.form.ShowInTaskbar = true;
					}
				}
			}
		};

		private bool caption = true;
		private bool lastfound = false;
		private bool owndialogs = false;
		private bool resizable = false;
#if !WINDOWS
		// Whether this GUI provides the standard macOS application menu bar (App/Edit/Window), which is
		// what makes Cmd+C/V/X/A/Z work in its text controls. Toggled by the "AppMenu" option; on by default.
		private bool includeAppMenu = true;
#endif
		private bool showCalled = false;

		public object BackColor
		{
			get => (form.BackColor.ToArgb() & 0x00FFFFFF).ToString("X6");

			set
			{
				Color c;

				if (value is string s)
				{
					if (!Conversions.TryParseColor(s, out c))
					{
						_ = Errors.ValueErrorOccurred($"Invalid background color {value}");
						return;
					}
				}
				else if (value is long ll)
				{
					// Preserve an explicit alpha byte so a script can request a translucent window numerically
					// (e.g. 0x80FF0000 = 50%-opacity red); a bare 0xRRGGBB (alpha byte 0) stays fully opaque as
					// before. For a fully transparent (alpha 0) background use the "Transparent" string or an
					// 8-digit hex string like "0x00RRGGBB", since 0 in the alpha byte is indistinguishable from a
					// plain RGB value here.
					c = Color.FromArgb((ll & 0xFF000000L) != 0 ? (int)ll : (int)(ll | 0xFF000000L));
				}
				else
				{
					_ = Errors.ValueErrorOccurred($"Invalid background color {value}");
					return;
				}

#if WINDOWS
				// WinForms has no per-pixel window-background alpha, so approximate the requested alpha. Set before Show.
				//  - alpha == 0  -> fully transparent: a colour key (the classic click-through hole).
				//  - 0 < alpha < 255 -> whole-window translucency via Form.Opacity (a layered window). NOTE this differs
				//    from Linux/macOS, where the alpha applies per-pixel to the BACKGROUND only (controls stay opaque);
				//    on Windows the whole window (controls included) becomes translucent — the closest WinForms can get,
				//    and an accepted cross-platform difference. (Shares the layered-window alpha with WinSetTransparent.)
				//  - alpha == 255 -> solid: clear any prior colour key / opacity so the window is fully opaque again.
				if (c.A == 0)
				{
					// Clear any residual whole-window translucency from a prior 0<alpha<255 set so the result is
					// fully opaque except for the colour-keyed hole (guard !=1.0 to leave an explicit WinSetTransparent
					// alone, matching the alpha==255 branch below).
					if (form.Opacity != 1.0)
						form.Opacity = 1.0;

					var key = Color.FromArgb(255, c.R, c.G, c.B);
					form.BackColor = key;
					form.TransparencyKey = key;
				}
				else if (c.A < 255)
				{
					if (form.TransparencyKey != Color.Empty)
						form.TransparencyKey = Color.Empty;

					form.BackColor = Color.FromArgb(255, c.R, c.G, c.B);
					form.Opacity = c.A / 255.0;
				}
				else
				{
					if (form.TransparencyKey != Color.Empty)
						form.TransparencyKey = Color.Empty;

					if (form.Opacity != 1.0)
						form.Opacity = 1.0;

					form.BackColor = c;
				}
#else
				// Eto: a background whose alpha < 1 switches the window to an RGBA visual (true per-pixel
				// transparency) on a composited desktop. Must be set before Show() so the visual is chosen at
				// realize time.
				form.BackColor = c;
#endif
			}
		}

		public object FocusedCtrl => form.ActiveControl ?? (object)DefaultObject;

		public long Hwnd => form.Handle;

		public long MarginX
		{
			get
			{
				EnsureDefaultMargins();
				return form.Margin.Left;
			}
			set
			{
				EnsureDefaultMargins();
				form.Margin = new Padding((int)value, form.Margin.Top, (int)value, form.Margin.Bottom);
			}
		}

		public long MarginY
		{
			get
			{
				EnsureDefaultMargins();
				return form.Margin.Top;
			}
			set
			{
				EnsureDefaultMargins();
				form.Margin = new Padding(form.Margin.Left, (int)value, form.Margin.Right, (int)value);
			}
		}

		public object MenuBar
		{
			get => menuBar ?? (object)DefaultObject;

			set
			{
				menuBar = (MenuBar)value;
#if WINDOWS
				form.TagAndAdd(menuBar.MenuStrip);
				form.MainMenuStrip = menuBar.MenuStrip;
#else
				menuBar.MenuStrip.SyncEtoMenuBar();
				var systemItems = Eto.Forms.MenuBarSystemItems.None;

				if (includeAppMenu)
				{
					systemItems = Eto.Forms.MenuBarSystemItems.Quit;

					// Eto versions differ: some expose Edit separately, while this fork folds it into Common.
					systemItems |= Enum.TryParse("Edit", out Eto.Forms.MenuBarSystemItems editItems)
						? editItems
						: Eto.Forms.MenuBarSystemItems.Common;
				}

				menuBar.MenuStrip.EtoMenuBar.IncludeSystemItems = systemItems;
				form.Menu = menuBar.MenuStrip.EtoMenuBar;
				// form.Menu assignment ran Eto's one-time CreateSystemMenu (OnPreLoad); from now on
				// SyncEtoMenuBar must re-merge the system menu itself after it rebuilds the items.
				menuBar.MenuStrip.MarkSystemMenuLoaded();
				form.MainMenuStrip = menuBar.MenuStrip;
#endif
			}
		}

		public object Name
		{
			get => form.Name;
			set => form.Name = value.ToString();
		}

		internal KeysharpForm ParentForm => form.FindParent<KeysharpForm>();

		public object Title
		{
			get => form.Text;
			set => form.Text = value.As();
		}

		public object Visible
		{
			get => form.Visible;
			set => form.Visible = value.Ab();
		}

		internal Font Font { get; set; }

		internal Forms.Control LastContainer
		{
			get => lastContainer;
			set => lastContainer = NormalizeContainer(value);
		}

		private Forms.Control lastContainer;

		private static Forms.Control NormalizeContainer(Forms.Control container)
		{
#if WINDOWS
			return container;
#else
			if (container is PixelLayout layout && layout.Parent is Forms.Control parent)
				return parent;

			return container;
#endif
		}

		internal Forms.Control LastControl
		{
			get
			{
				if (LastContainer != null)
				{
					Forms.Control lastControl = null;
					int maxIndex = int.MinValue;

					var container = LastContainer?.GetLayoutContainer();
					if (container == null)
						return null;

					foreach (Forms.Control ctrl in container.Controls)
					{
						if (ctrl is KeysharpStatusStrip)
							continue;

						if (ctrl.Tag is GuiTag tag)
						{
							if (tag.Index > maxIndex)
							{
								maxIndex = tag.Index;
								lastControl = ctrl;
							}
						}
					}

					return lastControl;
				}

				return null;
			}
		}

		//AHK's mSectionX/mSectionY and mMaxExtentRightSection/DownSection, kept as one rectangle per
		//container: positions here are container-relative (a Tab3 page is its own space, see the tab
		//display area note in AddControl), so a single running rectangle would mix coordinate spaces
		//whenever UseTab changes containers.
		private readonly Dictionary<Forms.Control, Rectangle> sections = [];

		internal StatusStrip StatusStrip { get; set; }

		public Gui(params object[] args) : base(args) { }

		internal Gui(object obj0 = null, object obj1 = null, object obj2 = null, object obj3 = null) : base(null)//The last parameter is hidden and is only for internal use for when we wrap the main window in a Gui object.
		{
			Script.InvokeOnUIThread(() =>
			{
				var script = Script.TheScript;

				if (obj3 is KeysharpForm kf)
				{
					form = kf;

					foreach (var ctrl in form.GetAllControlsRecursive<Forms.Control>())//In order for searches that use allGuiHwnds, we must make all of the child controls point here.
						ctrl.Tag = new Gui.Control(this, ctrl, ctrl.Name, true);//Supposed to be name like "label", "edit" etc, but just pass the name since this is only used with the main window.
				}
#if !WINDOWS
				// Preserve prebuilt window content (e.g. main window tabs/menu container).
				form.Content ??= new PixelLayout();
#endif
				LastContainer = form;
				form.Register(this);//Calling handle forces the creation of the window.

				if (lastfound)
					script.HwndLastUsed = Hwnd;
			});
		}

		~Gui()
		{
			//Re-check persistence when a Gui is collected, since it may have been the only thing keeping the
			//script alive. This runs on the GC finalizer thread, so it must never take the inline path:
			//ExitIfNotPersistent posts to the UI thread, but PostToUIThread falls back to running the action
			//inline when no UI context is bound, which would drive ExitAppInternal -- and its
			//GC.WaitForPendingFinalizers() -- from the finalizer thread and deadlock. Skip once the script has
			//exited too: TheScript is still set after teardown, so a late collection would otherwise run exit
			//logic against a dead script (or, after a restart, against a different one).
			var script = Script.TheScript;

			if (script == null || script.hasExited || script.UIThreadContext == null)
				return;

			script.ExitIfNotPersistent();
		}

		public KeysharpFunc __Enum(object count) => CreateEnumerator(count.Ai());

		public object __New(object Options = null, object Title = null, object EventObj = null)
		{
			if (form == null)//Don't allow derived classes to init twice.
			{
				Script.InvokeOnUIThread(() =>
				{
					var options = Options != null ? Options.As() : null;
					var caption = Title != null ? Title.As() : null;
					var eventObj = EventObj;
					var script = Script.TheScript;
					var newCount = Interlocked.Increment(ref script.GuiData.windowCount);
					//Get numeric creation params first.
					int addStyle = 0, addExStyle = 0, removeStyle = 0, removeExStyle = 0;
					Opt(options, ref addStyle, ref addExStyle, ref removeStyle, ref removeExStyle);
					form = new KeysharpForm(addStyle, addExStyle, removeStyle, removeExStyle)
					{
						eventObj = eventObj,
						FormBorderStyle = FormBorderStyle.FixedSingle,//Default to a non-resizeable window, with the maximize box disabled.
						Icon = TheScript.normalIcon,
						Name = $"Keysharp window {newCount}",
						MaximizeBox = false,
						SizeGripStyle = SizeGripStyle.Hide,
						Tag = new WeakReference<Gui>(this, false),
						Text = caption != "" ? caption : A_ScriptName
					};
					//Note that we don't do any Suspend/Resume layout calls when creating controls on the form as would normally
					//be done in designer-generated code. It appears to cause layout problems.
					_ = Opt(options);
					var formHandle = form.Handle;//Force the creation.
					var handleStr = $"{formHandle}";
#if !WINDOWS
					// Ensure the Eto content container exists before we size it later.
					form.Content ??= new PixelLayout();
#endif
					LastContainer = form;

#if !WINDOWS
					Keysharp.Internals.Window.Unix.EtoMessageSource.Attach(form);
#endif
					form.Register(this);//Calling handle forces the creation of the window.

					if (lastfound)
						script.HwndLastUsed = Hwnd;
				});
			}

			return DefaultObject;
		}

		void EnsureDefaultMargins()
		{
			if (marginsInit) return;
			float dpi = (float)(DpiScale * 96.0);
			float dpiinv = 96F / dpi;
			float fh = form.Font.GetHeight(dpi) * dpiinv;
			int mx = (int)Math.Ceiling(fh * 1.25f);
			int my = (int)Math.Ceiling(fh * 0.75f);
			form.Margin = new Padding(mx, my, mx, my);
			marginsInit = true;
		}

		public object Add(object controlType, object options = null, object text = null)
		{
			EnsureDefaultMargins();
			var typeo = controlType.As();
			var optionsStr = options.As();
			var o = text;//The third argument needs to account for being an array in the case of combo/list boxes.
			var type = typeo.ToLowerInvariant();
			Control holder = null;
			var textStr = o as string;

			if (textStr != null)
				textStr = textStr.ReplaceLineEndings(Environment.NewLine);

			var al = o as Array;
			var dpiscale = DpiScale;
			var dpiinv = 1.0 / dpiscale;
			var opts = ParseOpt(type, textStr, optionsStr);
			Forms.Control ctrl = null;

			switch (type)
			{
				case Keyword_Text:
				{
					// AHK uses SS_CENTERIMAGE (0x200) on static/Text controls to vertically
					// center single-line text. WinForms Labels ignore that Win32 style for
					// layout (they use TextAlign), so translate it into vertical centering.
					const int SS_CENTERIMAGE = 0x200;
					if ((opts.addstyle & SS_CENTERIMAGE) != 0 && !opts.valign.HasValue)
						opts.valign = GuiOptions.VerticalAlignment.Middle;

					var lbl = new KeysharpLabel(opts.addstyle, opts.addexstyle, opts.remstyle, opts.remexstyle)
					{
						Font = Conversions.ConvertFont(form.Font),
#if !WINDOWS
						Wrap = !opts.wordwrap.HasValue || opts.wordwrap.Value ? Forms.WrapMode.Word : Forms.WrapMode.None,
#endif
						//UseCompatibleTextRendering = true // Using this will cause some fonts to display boxes instead of the proper characters
					};
					ctrl = lbl;
					holder = new Text(this, ctrl, typeo);
				}
				break;

				case Keyword_Edit:
				{
					var ml = opts.multiline.IsTrue() || opts.rows > 1 || opts.height != int.MinValue;
#if WINDOWS

					if (opts.number)
						opts.addstyle |= WindowsAPI.ES_NUMBER;

					if (opts.limit == int.MinValue && !ml)
						opts.remstyle |= WindowsAPI.WS_HSCROLL | WindowsAPI.ES_AUTOHSCROLL;

					var txt = new KeysharpTextBox(opts.addstyle, opts.addexstyle, opts.remstyle, opts.remexstyle)
					{
						AcceptsTab = opts.wanttab ?? false,
						AcceptsReturn = opts.wantreturn ?? false,
						Multiline = ml,
						ReadOnly = opts.rdonly ?? false,
						WordWrap = ml,
						Font = Conversions.ConvertFont(form.Font)
					};

					if (opts.limit != int.MinValue)
						txt.MaxLength = opts.limit;

					if (opts.vscroll.IsTrue() && opts.hscrollamt != int.MinValue)
						txt.ScrollBars = ScrollBars.Both;
					else if (opts.vscroll.IsTrue() || txt.Multiline)
						txt.ScrollBars = ScrollBars.Vertical;
					else if (opts.hscrollamt != int.MinValue)
						txt.ScrollBars = ScrollBars.Horizontal;

					if (opts.lowercase.IsTrue())
						txt.CharacterCasing = CharacterCasing.Lower;
					else if (opts.uppercase.IsTrue())
						txt.CharacterCasing = CharacterCasing.Upper;
					else
						txt.CharacterCasing = CharacterCasing.Normal;

					if (opts.pwd)
					{
						if (opts.pwdch != "")
							txt.PasswordChar = opts.pwdch[0];
						else
							txt.UseSystemPasswordChar = true;
					}

					if (opts.wantctrla.IsFalse())
					{
						txt.PreviewKeyDown += SuppressCtrlAPreviewKeyDown;
						txt.KeyDown += SuppressCtrlAKeyDown;
					}

					if (txt.Multiline && opts.tabstops.Any())
						_ = WindowsAPI.SendMessage(txt.Handle, WindowsAPI.EM_SETTABSTOPS, opts.tabstops.Count, opts.tabstops.ToArray());

					ctrl = txt;
#else
					if (!ml) {
						KeysharpTextBox txt = null;
						KeysharpPasswordBox ptxt = null;

						if (opts.pwd)
							ptxt = new KeysharpPasswordBox(opts.addstyle, opts.addexstyle, opts.remstyle, opts.remexstyle);
						else
							txt = new KeysharpTextBox(opts.addstyle, opts.addexstyle, opts.remstyle, opts.remexstyle);

						if (txt != null)
						{
							txt.ReadOnly = opts.rdonly ?? false;
							txt.Font = Conversions.ConvertFont(form.Font);
						}
						else
						{
							ptxt.ReadOnly = opts.rdonly ?? false;
							ptxt.Font = Conversions.ConvertFont(form.Font);
						}

						if (opts.number)
						{
							if (txt != null)
								txt.IsNumeric = true;
							else
								ptxt.IsNumeric = true;
						}

						if (opts.limit != int.MinValue)
						{
							if (txt != null)
								txt.MaxLength = opts.limit;
							else
								ptxt.MaxLength = opts.limit;
						}

						if (opts.lowercase.IsTrue())
						{
							if (txt != null)
								txt.CharacterCasing = CharacterCasing.Lower;
							else
								ptxt.CharacterCasing = CharacterCasing.Lower;
						}
						else if (opts.uppercase.IsTrue())
						{
							if (txt != null)
								txt.CharacterCasing = CharacterCasing.Upper;
							else
								ptxt.CharacterCasing = CharacterCasing.Upper;
						}
						else
						{
							if (txt != null)
								txt.CharacterCasing = CharacterCasing.Normal;
							else
								ptxt.CharacterCasing = CharacterCasing.Normal;
						}

						if (opts.pwd)
						{
							if (opts.pwdch != "")
								ptxt.PasswordChar = opts.pwdch[0];
							else
								ptxt.UseSystemPasswordChar = true;
						}

						if (opts.wantctrla.IsFalse())
						{
							if (txt != null)
								txt.KeyDown += SuppressCtrlAKeyDown;
							else
								ptxt.KeyDown += SuppressCtrlAKeyDown;
						}
						ctrl = txt ?? (Forms.Control)ptxt;
					}
					else
					{
						var txt = new KeysharpTextArea(opts.addstyle, opts.addexstyle, opts.remstyle, opts.remexstyle)
						{
							AcceptsTab = opts.wanttab ?? true,
							AcceptsReturn = opts.wantreturn ?? true,
							Multiline = ml,
							ReadOnly = opts.rdonly ?? false,
							WordWrap = ml,
							Font = Conversions.ConvertFont(form.Font)
						};

						if (opts.number)
							txt.IsNumeric = true;

						if (opts.limit != int.MinValue)
							txt.MaxLength = opts.limit;

						if (opts.lowercase.IsTrue())
							txt.CharacterCasing = CharacterCasing.Lower;
						else if (opts.uppercase.IsTrue())
							txt.CharacterCasing = CharacterCasing.Upper;
						else
							txt.CharacterCasing = CharacterCasing.Normal;

						if (opts.pwd)
						{
							if (opts.pwdch != "")
								txt.PasswordChar = opts.pwdch[0];
							else
								txt.UseSystemPasswordChar = true;
						}

						if (opts.wantctrla.IsFalse())
						{
							txt.KeyDown += SuppressCtrlAKeyDown;
						}
						ctrl = txt;
					}

#endif
					holder = new Edit(this, ctrl, typeo);
				}
				break;

				case Keyword_Rich_Edit:
				{
					var ml = !opts.multiline.IsFalse();
#if WINDOWS

					if (opts.number)
						opts.addstyle |= WindowsAPI.ES_NUMBER;

					if (opts.limit == int.MinValue && !ml)
						opts.remstyle |= WindowsAPI.WS_HSCROLL | WindowsAPI.ES_AUTOHSCROLL;

#endif
					var txt = new KeysharpRichEdit(opts.addstyle, opts.addexstyle, opts.remstyle, opts.remexstyle)
					{
						AcceptsTab = opts.wanttab ?? false,
						Multiline = ml,
						ReadOnly = opts.rdonly ?? false,
						Font = Conversions.ConvertFont(form.Font)
					};
#if !WINDOWS

					if (opts.number)
						txt.IsNumeric = true;

#endif

					if (opts.limit != int.MinValue)
						txt.MaxLength = opts.limit;

					if (opts.vscroll.IsTrue() && opts.hscrollamt != int.MinValue)
						txt.ScrollBars = RichTextBoxScrollBars.Both;
					else if (opts.vscroll.IsTrue() || txt.Multiline)
						txt.ScrollBars = RichTextBoxScrollBars.Vertical;
					else if (opts.hscrollamt != int.MinValue)
						txt.ScrollBars = RichTextBoxScrollBars.Horizontal;

					if (opts.lowercase.IsTrue())
						txt.CharacterCasing = CharacterCasing.Lower;
					else if (opts.uppercase.IsTrue())
						txt.CharacterCasing = CharacterCasing.Upper;
					else
						txt.CharacterCasing = CharacterCasing.Normal;

					if (opts.wantctrla.IsFalse())
					{
#if WINDOWS
						txt.PreviewKeyDown += SuppressCtrlAPreviewKeyDown;
#endif
						txt.KeyDown += SuppressCtrlAKeyDown;
					}

#if WINDOWS

					if (txt.Multiline && opts.tabstops.Any())
						_ = WindowsAPI.SendMessage(txt.Handle, WindowsAPI.EM_SETTABSTOPS, opts.tabstops.Count, opts.tabstops.ToArray());

#endif
					ctrl = txt;
					holder = new RichEdit(this, ctrl, typeo);
				}
				break;

				case Keyword_UpDown:
				{
					//This is done differently than how the documentation says.
					//There is no such thing as a "buddy". rather, the numeric up down control is entirely self
					//contained. This is because the buddy style control was a remnant of MFC, and C# doesn't support such a control.
					//Also, there is no way to do "wrapping". Users will need to program this themselves.
					//16 has no effect, no way to make it vertical.
					//Horz has no effect.
					//Min and max can't be swapped.
					//Increment is made easier to set with the new "Increment" option.
					//Hex is made easier with the new "Hex" option.
					var nud = new KeysharpNumericUpDown(opts.addstyle, opts.addexstyle, opts.remstyle, opts.remexstyle)
					{
						Increment = opts.nudinc ?? 1,
						ThousandsSeparator = (opts.addstyle & 0x80) != 0x80,
						UpDownAlign = opts.halign.HasValue && opts.halign.Value == GuiOptions.HorizontalAlignment.Left ? LeftRightAlignment.Left : LeftRightAlignment.Right,
						Hexadecimal = opts.hex.IsTrue(),
						Font = Conversions.ConvertFont(form.Font)
					};

					if (opts.nudlow.HasValue)
						nud.Minimum = opts.nudlow.Value;

					if (opts.nudhigh.HasValue)
						nud.Maximum = opts.nudhigh.Value;

#if WINDOWS
					if (text != null)
						nud.Value = (decimal)text.Ad();
					else
						nud.Value = Math.Min(nud.Minimum, 0m);
#else
					if (text != null)
						nud.Value = text.Ad();
					else
						nud.Value = Math.Min(nud.Minimum, 0d);
#endif

					ctrl = nud;
					holder = new UpDown(this, ctrl, typeo);
				}
				break;

				case Keyword_Pic:
				case Keyword_Picture://No special support for GDI+, instead we just use whatever C# uses under the hood for its PictureBox control. Also, animated gifs do animate.
				{
					opts.addstyle |= 0x20;
					var pic = new KeysharpPictureBox(textStr, opts.addstyle, opts.addexstyle, opts.remstyle, opts.remexstyle)
					{
						Font = Conversions.ConvertFont(form.Font)
					};//Attempt to support transparency.

					if (opts.width < 0 && opts.height < 0)
					{
						pic.SizeMode = PictureBoxSizeMode.AutoSize;
					}
					else if (opts.width < 0 || opts.height < 0)
					{
						if (opts.width < 0)
							pic.ScaleWidth = true;
						else if (opts.height < 0)
							pic.ScaleHeight = true;

						pic.SizeMode = PictureBoxSizeMode.Zoom;
					}
					else
						pic.SizeMode = PictureBoxSizeMode.StretchImage;

					ctrl = pic;
					holder = new Pic(this, ctrl, typeo);
				}
				break;

				case Keyword_Button:
				{
#if WINDOWS
					opts.addstyle |= WindowsAPI.BS_NOTIFY;//Documentation says BS_NOTIFY will be automatically added in OnEvent(), which is the only time clicks are handled, so add regardless.
#endif
					ctrl = new KeysharpButton(opts.addstyle, opts.addexstyle, opts.remstyle, opts.remexstyle)
					{
						Name = textStr,
						AutoSize = opts.width == int.MinValue && opts.wp == int.MinValue && opts.height == int.MinValue && opts.hp == int.MinValue,
						Font = Conversions.ConvertFont(form.Font)
					};
#if WINDOWS
					if (opts.btndef.IsTrue())
						form.AcceptButton = (IButtonControl)ctrl;
#endif

					holder = new Button(this, ctrl, typeo);
				}
				break;

				case Keyword_CheckBox:
				{
					var chk = new KeysharpCheckBox(opts.addstyle, opts.addexstyle, opts.remstyle, opts.remexstyle)
					{
						ThreeState = opts.check3,
						Font = Conversions.ConvertFont(form.Font)
					};

					if (opts.ischecked.HasValue)
					{
						if (opts.ischecked == 0)
							chk.Checked = false;
						else if (opts.ischecked == 1)
							chk.Checked = true;
						else if (opts.ischecked == -1 || opts.checkedgray)
							chk.CheckState = CheckState.Indeterminate;
					}

					if (opts.halign.GetValueOrDefault() == GuiOptions.HorizontalAlignment.Right)
						chk.CheckAlign = ContentAlignment.MiddleRight;

					ctrl = chk;
					holder = new CheckBox(this, ctrl, typeo);
				}
				break;

				case Keyword_Radio:
				{
#if !WINDOWS
					KeysharpRadioButton controller = null;
					if (!opts.group && LastControl is KeysharpRadioButton)
					{
						var radioLayoutContainer = LastContainer?.GetLayoutContainer();
						if (radioLayoutContainer != null)
							controller = radioLayoutContainer.Controls.OfType<KeysharpRadioButton>().FirstOrDefault();
					}

					KeysharpRadioButton rad = controller != null
						? new KeysharpRadioButton(controller, opts.addstyle, opts.addexstyle, opts.remstyle, opts.remexstyle)
						: new KeysharpRadioButton(opts.addstyle, opts.addexstyle, opts.remstyle, opts.remexstyle);
#else
					var rad = new KeysharpRadioButton(opts.addstyle, opts.addexstyle, opts.remstyle, opts.remexstyle);
#endif
					rad.AutoSize = true;
					rad.Text = textStr;
					rad.Font = Conversions.ConvertFont(form.Font);
					ctrl = rad;
					holder = new Radio(this, ctrl, typeo);
				}
				break;

				case Keyword_ComboBox:
				case Keyword_DropDownList:
                case Keyword_DDL:
                    {
					bool isCombo = type == Keyword_ComboBox;
					KeysharpComboBox ddl;

					if (!isCombo)
					{
						ddl = new KeysharpComboBox(opts.addstyle, opts.addexstyle, opts.remstyle, opts.remexstyle)
						{
							Font = Conversions.ConvertFont(form.Font)
						};
						ddl.DropDownStyle = ComboBoxStyle.DropDownList;
					}
					else
					{
#if WINDOWS
						if (opts.limit != int.MinValue) opts.remstyle |= WindowsAPI.CBS_AUTOHSCROLL;
#endif

						ddl = new KeysharpComboBox(opts.addstyle, opts.addexstyle, opts.remstyle, opts.remexstyle)
						{
							Font = Conversions.ConvertFont(form.Font)
						};
						ddl.DropDownStyle = opts.cmbsimple.IsTrue() ? ComboBoxStyle.Simple : ComboBoxStyle.DropDown;
					}

					if (opts.sort.IsTrue())
					{
						ddl.Sorted = true;

						if (isCombo)
						{
#if WINDOWS
							ddl.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
							ddl.AutoCompleteSource = AutoCompleteSource.ListItems;
#else
							ddl.AutoComplete = true;
#endif
						}
					}

					if (al != null)
						ddl.Items.AddRange(al.Cast<(object, object)>().Select(x => x.Item2).Select(x => opts.lowercase.IsTrue() ? x.Str().ToLower() : opts.uppercase.IsTrue() ? x.Str().ToUpper() : x.Str()).ToArray());

					if (opts.choose.Any())
						ddl.SelectedIndex = opts.choose[0];

					ddl.IntegralHeight = true;

					if (opts.rows != float.MinValue)
					{
						ddl.IntegralHeight = false;
						ddl.MaxDropDownItems = (int)opts.rows;
					}

					ctrl = ddl;
					holder = isCombo ? new ComboBox(this, ctrl, typeo) : new DDL(this, ctrl, typeo);
				}
				break;

				case Keyword_ListBox:
				{
#if WINDOWS

					if (opts.rdonly.IsTrue())
						opts.addstyle |= WindowsAPI.LBS_NOSEL;

#endif
					var lb = new KeysharpListBox(opts.addstyle, opts.addexstyle, opts.remstyle, opts.remexstyle)
					{
						SelectionMode = opts.multiline.IsTrue() ? SelectionMode.MultiExtended : SelectionMode.One,
						Sorted = opts.sort.IsTrue(),//Unsure how to make incremental search work.
						Font = Conversions.ConvertFont(form.Font)
					};

					if (al != null)
						lb.Items.AddRange(al.Cast<(object, object)>().Select(x => x.Item2).Select(x => opts.lowercase.IsTrue() ? x.Str().ToLower() : opts.uppercase.IsTrue() ? x.Str().ToUpper() : x.Str()).ToArray());

					if (opts.vscroll.HasValue)
						lb.ScrollAlwaysVisible = opts.vscroll.Value;

					if (opts.hscrollamt != int.MinValue)
						lb.HorizontalScrollbar = true;

					if (opts.hscrollamt > 0)
						lb.HorizontalExtent = opts.hscrollamt;

					if (opts.choose.Any())
					{
						if (lb.SelectionMode == SelectionMode.MultiExtended)
						{
							foreach (var ch in opts.choose)
								lb.SelectedIndices.Add(ch);
						}
						else if (lb.SelectionMode == SelectionMode.One)
							lb.SelectedIndex = opts.choose[0];
					}

					if (opts.tabstops.Any())
					{
						lb.UseCustomTabOffsets = true;
						lb.CustomTabOffsets.AddRange(opts.tabstops.ToArray());
					}

					if ((opts.addstyle & 0x100) == 0x100)
						lb.IntegralHeight = false;

					ctrl = lb;
					holder = new ListBox(this, ctrl, typeo);
				}
				break;

				case Keyword_ListView:
				{
					//There is no way to preallocate memory with the "Count" option, so that is ignored.
					var lv = new KeysharpListView(opts.addstyle, opts.addexstyle, opts.remstyle, opts.remexstyle)
					{
						Font = Conversions.ConvertFont(form.Font)
					};
					if (al != null)
						lv.Columns.AddRange(al.Cast<(object, object)>().Select(x => x.Item2).Select(x => new ColumnHeader { Text = x.Str() }).ToArray());
					lv.CheckBoxes = opts.ischecked.HasValue && opts.ischecked.Value > 0;
					lv.GridLines = opts.grid.IsTrue();
					lv.LabelEdit = opts.rdonly.IsFalse();
					lv.View = opts.lvview ?? View.Details;

#if !WINDOWS
					lv.SyncColumns();
					Reflections.SafeSetProperty(lv, "AllowF2Edit", !opts.wantf2.IsFalse());
#endif
					if (lv.LabelEdit && !opts.wantf2.IsFalse())//Note that checking !IsFalse() is not the same as IsTrue().
						lv.KeyDown += Tv_Lv_KeyDown;

					if (opts.sort.IsTrue())
						lv.Sorting = SortOrder.Ascending;
					else if (opts.sortdesc.IsTrue())
						lv.Sorting = SortOrder.Descending;
					else
						lv.Sorting = SortOrder.None;

					if (opts.multiline.HasValue)
						lv.MultiSelect = opts.multiline.Value;

					if ((opts.addlvstyle & 0x10) == 0x10)
						lv.AllowColumnReorder = true;
					else if ((opts.remlvstyle & 0x10) == 0x10)
						lv.AllowColumnReorder = false;

					if ((opts.addlvstyle & 0x20) == 0x20)
						lv.FullRowSelect = true;
					else if ((opts.remlvstyle & 0x20) == 0x20)
						lv.FullRowSelect = false;

					if (opts.header.HasValue)
						lv.HeaderStyle = opts.header.IsFalse() ? ColumnHeaderStyle.None : ColumnHeaderStyle.Clickable;
					else if (opts.clickheader.HasValue)
						lv.HeaderStyle = opts.clickheader.IsFalse() ? ColumnHeaderStyle.Nonclickable : ColumnHeaderStyle.Clickable;
#if WINDOWS
					else if (opts.sortheader.HasValue)
						lv.HeaderStyle = opts.sortheader.IsFalse() ? ColumnHeaderStyle.Nonclickable : ColumnHeaderStyle.Clickable;
#else
					if (opts.sortheader.HasValue)
						lv.AutoSortHeader = opts.sortheader.IsTrue();
					else if (opts.clickheader.HasValue && opts.clickheader.IsFalse())
						lv.AutoSortHeader = false;
					else
						lv.AutoSortHeader = true;
#endif

					lv.AutoResizeColumns(ColumnHeaderAutoResizeStyle.HeaderSize);
                    ctrl = lv;
					holder = new ListView(this, ctrl, typeo);
				}
				break;

				case Keyword_TreeView:
				{
#if WINDOWS
					var tv = new KeysharpTreeView(!opts.hscroll ? (opts.addstyle | WindowsAPI.TVS_NOHSCROLL) : opts.addstyle, opts.addexstyle, opts.remstyle, opts.remexstyle);

                    if (opts.buttons.HasValue)
						tv.ShowPlusMinus = opts.buttons.Value;

					tv.CheckBoxes = opts.ischecked.HasValue && opts.ischecked.Value != 0;
					tv.ShowLines = opts.lines ?? true;
					tv.LabelEdit = opts.rdonly.IsFalse();
					tv.HideSelection = false;

					if (tv.LabelEdit && !opts.wantf2.IsFalse())//Note that checking !IsFalse() is not the same as IsTrue().
						tv.KeyDown += Tv_Lv_KeyDown;
#else
					var tv = new KeysharpTreeView(opts.addstyle, opts.addexstyle, opts.remstyle, opts.remexstyle);
					tv.CheckBoxes = opts.ischecked.HasValue && opts.ischecked.Value != 0;
					if (opts.buttons.HasValue)
						tv.ShowPlusMinus = opts.buttons.Value;

					if (opts.lines.HasValue)
						tv.ShowLines = opts.lines.Value;

					tv.LabelEdit = opts.rdonly.IsFalse();
					tv.HideSelection = false;

					if (tv.LabelEdit && !opts.wantf2.IsFalse())//Note that checking !IsFalse() is not the same as IsTrue().
						tv.KeyDown += Tv_Lv_KeyDown;
#endif
					tv.Font = Conversions.ConvertFont(form.Font);

					if (opts.ilid != long.MinValue && ImageLists.IL_Get(opts.ilid) is ImageList il)
						tv.ImageList = il;

					ctrl = tv;
					holder = new TreeView(this, ctrl, typeo);
				}
				break;

				case Keyword_Link:
				{
					var linklabel = new KeysharpLinkLabel(textStr, opts.addstyle, opts.addexstyle, opts.remstyle, opts.remexstyle)
					{
						Font = Conversions.ConvertFont(form.Font)
					};
					ctrl = linklabel;
					holder = new Link(this, ctrl, typeo);
				}
				break;

				case Keyword_Hotkey:
				{
					var hk = new HotkeyBox()
					{
						Font = Conversions.ConvertFont(form.Font)
					};

					if (opts.limit != int.MinValue)
						hk.Limit = (HotkeyBox.Limits)opts.limit;

					if (!string.IsNullOrEmpty(textStr))
						hk.Text = textStr;

					ctrl = hk;
					holder = new Hotkey(this, ctrl, typeo);
				}
				break;

				case Keyword_DateTime:
				{
					var dtp = new KeysharpDateTimePicker(opts.addstyle, opts.addexstyle, opts.remstyle, opts.remexstyle)
					{
						Font = Conversions.ConvertFont(form.Font)
					};
					dtp.SetFormat(textStr);

#if WINDOWS
					if (opts.halign.HasValue && opts.halign == GuiOptions.HorizontalAlignment.Right)
						dtp.DropDownAlign = LeftRightAlignment.Right;

					dtp.ShowUpDown = opts.dtopt1;

					if (opts.c.HasValue)
						dtp.CalendarForeColor = opts.c.Value;//This will only have an effect if visual styles are disabled.
#endif

					if (opts.dtlow != System.DateTime.MinValue)
						dtp.MinDate = opts.dtlow;

					if (opts.dthigh != System.DateTime.MaxValue)
						dtp.MaxDate = opts.dthigh;

#if WINDOWS
					if (opts.choosenone)
					{
						dtp.ShowCheckBox = true;
						dtp.Checked = false;
						var origfmt = dtp.Format;
						var origcustomfmt = dtp.CustomFormat;
						dtp.Format = DateTimePickerFormat.Custom;
						dtp.CustomFormat = " ";
						dtp.ValueChanged += (oo, ee) =>
						{
							if (!string.IsNullOrEmpty(origcustomfmt))
								dtp.CustomFormat = origcustomfmt;
							else
								dtp.Format = origfmt;
						};
					}
					else
					{
						dtp.ShowCheckBox = opts.dtopt2;
						dtp.Checked = true;

						if (opts.dtChoose >= dtp.MinDate && opts.dtChoose <= dtp.MaxDate)
							dtp.Value = opts.dtChoose;
					}
#endif

					ctrl = dtp;
					holder = new DateTime(this, ctrl, typeo);
				}
				break;

				case Keyword_MonthCal:
				{
					var cal = new KeysharpMonthCalendar(opts.addstyle, opts.addexstyle, opts.remstyle, opts.remexstyle)
					{
						Font = Conversions.ConvertFont(form.Font)
					};

					if (opts.dtlow != System.DateTime.MinValue)//This causes a crash when you scroll past it.
						cal.MinDate = opts.dtlow;

					if (opts.dthigh != System.DateTime.MaxValue)
						cal.MaxDate = opts.dthigh;

#if WINDOWS
					cal.ShowWeekNumbers = opts.opt4;
					cal.ShowTodayCircle = !opts.opt8;
					cal.ShowToday = !opts.opt16;
					cal.MaxSelectionCount = opts.datemultisel ? 31 : 1;

					if (opts.dtselstart > System.DateTime.MinValue && opts.dtselend < System.DateTime.MaxValue)
						cal.SelectionRange = new SelectionRange(opts.dtselstart, opts.dtselend);

					//Note that colors do not work here is visual styles are enabled.
					if (opts.c.HasValue)
						cal.TitleForeColor = opts.c.Value;

					if (opts.bgcolor.HasValue)
						cal.TitleBackColor = opts.bgcolor.Value;
#endif

					ctrl = cal;
					holder = new MonthCal(this, ctrl, typeo);
				}
				break;

				case Keyword_Slider://Buddy controls are not supported.
				{
#if WINDOWS

					if (opts.tooltip)
						opts.addstyle |= WindowsAPI.TBS_TOOLTIPS;

					if (opts.thick != int.MinValue)
						opts.addstyle |= WindowsAPI.TBS_FIXEDLENGTH;

#endif
					var slider = new KeysharpTrackBar(opts.addstyle, opts.addexstyle, opts.remstyle, opts.remexstyle)
					{
						Orientation = opts.vertical ? Orientation.Vertical : Orientation.Horizontal,
						Font = Conversions.ConvertFont(form.Font)
					};

					if (opts.nudlow.HasValue && opts.nudhigh.HasValue)
					{
						slider.Minimum = opts.nudlow.Value;
						slider.Maximum = opts.nudhigh.Value;
					}
					else//Set the same defaults as AHK.
					{
						slider.Minimum = 0;
						slider.Maximum = 100;
					}

					if (o != null)
						slider.Value = o.Ai();

					if (opts.halign.HasValue && opts.halign.Value == GuiOptions.HorizontalAlignment.Center)
						slider.TickStyle = TickStyle.Both;
					else if (opts.halign.HasValue && opts.halign.Value == GuiOptions.HorizontalAlignment.Left)
						slider.TickStyle = TickStyle.TopLeft;
					else if (opts.noticks.IsTrue())
						slider.TickStyle = TickStyle.None;

					if (opts.tickinterval != int.MinValue)
						slider.TickFrequency = opts.tickinterval;
					else
						slider.TickFrequency = slider.Maximum - slider.Minimum;

					if (opts.line != int.MinValue)
						slider.SmallChange = opts.line;

					if (opts.page != int.MinValue)
						slider.LargeChange = opts.page;

#if WINDOWS

					if (opts.thick != int.MinValue)
						_ = WindowsAPI.SendMessage(slider.Handle, WindowsAPI.TBM_SETTHUMBLENGTH, (uint)opts.thick, 0);

					if (opts.tooltip)
						_ = WindowsAPI.SendMessage(slider.Handle, WindowsAPI.TBM_SETTIPSIDE, (uint)opts.tooltipside, 0);

#endif
					slider.inverted = opts.invert.IsTrue();
					ctrl = slider;
					holder = new Slider(this, ctrl, typeo);
				}
				break;

				case Keyword_Progress:
				{
					if (opts.vertical)
						opts.addstyle |= 0x04;

					bool smooth = opts.smooth.IsTrue();

					var prg = new KeysharpProgressBar(smooth || opts.bgcolor.HasValue || opts.c != Forms.Control.DefaultForeColor,
													  opts.addstyle, opts.addexstyle, opts.remstyle, opts.remexstyle)
					{
						Font = Conversions.ConvertFont(form.Font)
					};
					prg.Style = smooth ? ProgressBarStyle.Continuous : ProgressBarStyle.Blocks;

					if (opts.nudlow.HasValue)
						prg.Minimum = opts.nudlow.Value;

					if (opts.nudhigh.HasValue)
						prg.Maximum = opts.nudhigh.Value;

					if (o is long pos)
						prg.Value = (int)pos;

					if (opts.bgcolor.HasValue)
						prg.BackColor = opts.bgcolor.Value;

#if !WINDOWS
					if (opts.c.HasValue)
						prg.BarColor = opts.c.Value;
#endif

#if WINDOWS

					if (opts.vertical && opts.width == int.MinValue && opts.height == int.MinValue)
					{
						var prgSize = prg.GetSize();
						prg.SetSize(new Size(prgSize.Height, prgSize.Width));
					}

#endif
					ctrl = prg;
					holder = new Progress(this, ctrl, typeo);
				}
				break;

				case Keyword_GroupBox:
				{
					ctrl = new KeysharpGroupBox(opts.addstyle, opts.addexstyle, opts.remstyle, opts.remexstyle)
					{
						Font = Conversions.ConvertFont(form.Font)
					};
					holder = new GroupBox(this, ctrl, typeo);
				}
				break;

				case Keyword_Tab:
				case Keyword_Tab2:
				case Keyword_Tab3:
				{
					var kstc = new KeysharpTabControl(opts.addstyle, opts.addexstyle, opts.remstyle, opts.remexstyle)
					{
						Font = Conversions.ConvertFont(form.Font)
					};//This will also support image lists just like TreeView for setting icons on tabs, instead of using SendMessage().
					if (al != null)
#if WINDOWS
						kstc.TabPages.AddRange(al.Cast<(object, object)>().Select(x => x.Item2).Select(x => new TabPage(x.Str())).ToArray());
#else
					{
						var pages = al.Cast<(object, object)>()
							.Select(x => x.Item2.Str())
							.Select(pageText => new TabPage
							{
								Text = KeysharpTabControl.DisplayText(pageText),
								Content = new PixelLayout()
							})
							.ToArray();
						kstc.TabPages.AddRange(pages);
					}
#endif
					if (opts.halign.HasValue)
					{
						if (opts.halign.Value == GuiOptions.HorizontalAlignment.Left)
							kstc.Alignment = TabAlignment.Left;
						else if (opts.halign.Value == GuiOptions.HorizontalAlignment.Right)
							kstc.Alignment = TabAlignment.Right;
					}
					if (opts.valign.HasValue)
					{
						if (opts.valign.Value == GuiOptions.VerticalAlignment.Bottom)
							kstc.Alignment = TabAlignment.Bottom;
						else if (opts.valign.Value == GuiOptions.VerticalAlignment.Top)
							kstc.Alignment = TabAlignment.Top;
					}

					if (opts.buttons.HasValue)
						kstc.Appearance = TabAppearance.FlatButtons;

					if (opts.choose.Any())
						kstc.SelectedIndex = opts.choose[0];

					if (opts.wordwrap.HasValue)
						kstc.Multiline = opts.wordwrap.IsTrue();

					if (opts.bgcolor.HasValue)
						kstc.SetColor(opts.bgcolor.Value);
					else if (form.BackColor != SystemColors.Control)
						kstc.SetColor(form.BackColor);

					ctrl = kstc;
					holder = new Tab(this, ctrl, typeo);
				}
				break;

				case Keyword_StatusBar:
				{
					var ss = new KeysharpStatusStrip(opts.addstyle, opts.addexstyle, opts.remstyle, opts.remexstyle)
					{
						Font = Conversions.ConvertFont(form.Font)
					};
					StatusStrip = ss;
					ss.AutoSize = false;
					ss.ImageScalingSize = new Size(Convert.ToInt32(28 * dpiscale), Convert.ToInt32(28 * dpiscale));
					ss.Dock = DockStyle.Bottom;//Docking must be used and must be on the bottom. Don't ever set form.AutoSize = true with this, they are incompatible.
					ss.SizingGrip = false;
					if (opts.bgcolor.HasValue)
						ss.BackColor = opts.bgcolor.Value;

					if (!string.IsNullOrEmpty(textStr))
					{
						var tsl = new KeysharpToolStripStatusLabel(textStr)
						{
							AutoSize = true,
							Name = $"AutoToolStripLabel{ss.Items.Count}",
							Font = Conversions.ConvertFont(form.Font)
						};

						_ = ss.Items.Add(tsl);
					}

					ctrl = ss;
					holder = new StatusBar(this, ctrl, typeo);
				}
				break;
#if WINDOWS

				case Keyword_ActiveX:
				{
					var ax = new KeysharpActiveX(textStr)
					{
						Font = Conversions.ConvertFont(form.Font)
					};
					ctrl = ax;
					holder = new ActiveX(this, ctrl, typeo);
				}
				break;
#endif

				case Keyword_WebBrowser:
				{
					var web = new KeysharpWebBrowser()
					{
						Font = Conversions.ConvertFont(form.Font)
					};
					web.Navigate(textStr);
					ctrl = web;
					holder = new WebBrowser(this, ctrl, typeo);
				}
				break;
#if WINDOWS

				case Keyword_Custom:
				{
					var custom = new KeysharpCustomControl(opts.customclass, opts.addstyle, opts.addexstyle, opts.remstyle, opts.remexstyle);
					ctrl = custom;

					if (!opts.bgcolor.HasValue)
						ctrl.BackColor = Color.FromKnownColor(KnownColor.Window);

					holder = new Custom(this, ctrl, typeo);
				}
				break;
#endif
			}

			if (ctrl == null)
				return Errors.ValueErrorOccurred($"Invalid control type: {type}.");

			if (opts.name != null)
				ctrl.Name = opts.name;

			if (opts.wordwrap.HasValue)
				Reflections.SafeSetProperty(ctrl, "WordWrap", opts.wordwrap.Value);

#if WINDOWS
			if (opts.thinborder.HasValue)
				Reflections.SafeSetProperty(ctrl, "BorderStyle", opts.thinborder.Value ? BorderStyle.FixedSingle : BorderStyle.None);
#endif

			if (opts.autosize.HasValue)
				Reflections.SafeSetProperty(ctrl, "AutoSize", opts.autosize.Value);

			if (textStr != null && ctrl is not KeysharpDateTimePicker && ctrl is not HotkeyBox
			 && ctrl is not KeysharpLinkLabel)
				ctrl.Text = textStr;

			if (ctrl is not KeysharpStatusStrip)//Don't want status strip to have a margin, so it can be placed at the bottom of the form when autosize is true, and have it look exactly like it would if it were docked when autosize is false.
				ctrl.Margin = form.Margin;

			//ctrl.Padding = form.Padding;

			if (opts.enabled.HasValue)
				ctrl.Enabled = opts.enabled.Value;

			if (opts.c.HasValue)
				ctrl.ForeColor = opts.c.Value;
			else
				ctrl.ForeColor = form.ForeColor;

#if WINDOWS
			if (opts.tabstop.HasValue)
				ctrl.TabStop = opts.tabstop.Value;
#endif

			if (opts.bgtrans)
				ctrl.BackColor = Color.Transparent;
			else if (opts.bgcolor.HasValue)
				ctrl.BackColor = opts.bgcolor.Value;

			SetContentAlignment(ctrl, opts);

			// Apply per-control font overrides (sN / bold / italic / strike / underline / norm parsed from the
			// options string) on top of the inherited GUI font. Done before the control is attached and sized so
			// the PreferredSize/autosize logic below reflects the final font.
			if (!string.IsNullOrEmpty(opts.fontstyles))
				ctrl.Font = Conversions.ParseFont(form.Font, opts.fontstyles);

				var prevParent = LastContainer;

			if (opts.altsubmit.HasValue)
				holder.AltSubmit = opts.altsubmit.Value;

			var lastControl = LastControl;

			if (lastControl is KeysharpRadioButton && (ctrl is not KeysharpRadioButton || opts.group))//Pop container if we've ended a radio group.
			{
				LastContainer = LastContainer.GetLogicalParent();
				lastControl = LastControl;//Will retrieve the last control in the LastContainer we just assigned.
			}

			//Both read here because adding a Tab3 moves LastContainer on to its first page mid-add: the
			//section, like lastControl, belongs to the container this control lands in.
			var placementContainer = LastContainer;
			Rectangle? section = placementContainer != null && sections.TryGetValue(placementContainer, out var sect) ? sect : null;
			var isNewRadioGroup = ctrl is KeysharpRadioButton && (lastControl == null || lastControl is not KeysharpRadioButton || opts.group);
			var isTabControl = ctrl is KeysharpTabControl;
			var rbContainer = lastControl?.GetLogicalParent() ?? LastContainer;
			bool xSpecified = opts.xpos != GuiOptions.Positioning.None;
			bool ySpecified = opts.ypos != GuiOptions.Positioning.None;
			bool needsContainerMostForPositioning =
				(!ySpecified && (opts.xpos == GuiOptions.Positioning.Absolute || opts.xpos == GuiOptions.Positioning.Container || opts.xpos == GuiOptions.Positioning.Margin))
				|| (!xSpecified && (opts.ypos == GuiOptions.Positioning.Absolute || opts.ypos == GuiOptions.Positioning.Container || opts.ypos == GuiOptions.Positioning.Margin));
			bool needsSectionMostForPositioning =
				section != null
				&& ((!ySpecified && opts.xpos == GuiOptions.Positioning.Section) || (!xSpecified && opts.ypos == GuiOptions.Positioning.Section));
			bool needsLastParentMostForPositioning = lastControl != null
				&& lastControl.Dock == DockStyle.None
				&& section == null
				&& ((!ySpecified && opts.xpos == GuiOptions.Positioning.Section) || (!xSpecified && opts.ypos == GuiOptions.Positioning.Section));
			(Forms.Control right, Forms.Control bottom) rbContainerMost = needsContainerMostForPositioning && rbContainer != null
				? rbContainer.RightBottomMost()
				: (null, null);
			(Forms.Control right, Forms.Control bottom) lastControlParentMost = needsLastParentMostForPositioning && lastControl?.Parent != null
				? lastControl.Parent.RightBottomMost()
				: (null, null);
			var layoutContainer = LastContainer?.GetLayoutContainer();
			var lastContainerChildCount = layoutContainer == null ? 0 : layoutContainer.Controls.Count();

			Panel radioGroupPanel = null;

			// Attach before sizing so PreferredSize/GetPreferredSize doesn't access unattached widgets.
			if (isNewRadioGroup)
			{
				radioGroupPanel = new Panel();
				LastContainer.TagAndAdd(radioGroupPanel);
				radioGroupPanel.TagAndAdd(holder);
			}
			else
			{
				var sizingParent = ctrl is KeysharpStatusStrip ? form : isTabControl ? prevParent : LastContainer;
				sizingParent.TagAndAdd(holder);
			}

			var fontpixels = GetFontPixels(ctrl.Font);// * dpiinv;
			var scaledPref = (double)ctrl.PreferredSize.Width;
			int finalWidth = -1, finalHeight = -1;
			var w = scaledPref;
			var widthAuto = opts.width == -1 && opts.wp == int.MinValue;
			var heightAuto = opts.height == -1 && opts.hp == int.MinValue;

			int lcLeft = 0, lcTop = 0, lcBottom = 0, lcRight = 0, lcWidth = 0, lcHeight = 0;
			if (lastControl != null)
			{
				var lcLoc = lastControl.GetLocation();
				var lcSize = lastControl.GetSize();
				lcLeft = lcLoc.X;
				lcTop = lcLoc.Y;
				lcRight = lcLoc.X + lcSize.Width;
				lcBottom = lcLoc.Y + lcSize.Height;
				lcWidth = lcSize.Width;
				lcHeight = lcSize.Height;
			}

			if (opts.autosize.IsTrue())
				goto heightdone;

			if (opts.wp != int.MinValue)
			{
				w = lastControl != null ? lcWidth + opts.wp * dpiscale : 0.0;
			}
			else if (opts.width != int.MinValue)
			{
				if (opts.width != -1)
					w = Convert.ToInt32(dpiscale * opts.width);
			}
			else if (ctrl is KeysharpProgressBar kpb && ((kpb.AddStyle & 0x04) == 0x04))
				w = fontpixels * 2;
			else if (ctrl is KeysharpNumericUpDown)
				w = lcWidth;
			else if (ctrl is KeysharpComboBox || ctrl is HotkeyBox || ctrl is KeysharpListBox || ctrl is KeysharpNumericUpDown || ctrl is KeysharpProgressBar || ctrl is KeysharpTextBox || ctrl is KeysharpPasswordBox)
				w = fontpixels * 15;
				else if (ctrl is KeysharpTrackBar trk)
				{
#if WINDOWS
					w = trk.Orientation == Orientation.Horizontal ? fontpixels * 15 : fontpixels * 2;//Documentation didn't mention a default for vertical trackbars, so just make it the same a vertical progress bar.
#else
					w = trk.Orientation == Orientation.Horizontal ? fontpixels * 16 : fontpixels * 3;
#endif
				}
			else if (ctrl is KeysharpGroupBox)
				w = fontpixels * 18;
			else if (ctrl is TabPage || ctrl is KeysharpTabControl)
				w = (fontpixels * 30) + (3 * ctrl.Margin.Left);
			else if (ctrl is KeysharpListView || ctrl is KeysharpTreeView || ctrl is KeysharpDateTimePicker)//Documentaiton doesn't mention these, but IronAHK handled them this way, so leaving this here.
				w = fontpixels * 30;

#if WINDOWS
			else if (ctrl is KeysharpCustomControl custom)
				w = fontpixels * 10;
#else
			else if (ctrl is KeysharpStatusStrip strip)
				w = form.GetSize().Width;
#endif
			if (widthAuto)
			{
				finalWidth = -1;
			}
			else if (opts.width == int.MinValue && opts.wp == int.MinValue)
			{
				finalWidth = Math.Max((int)w, Convert.ToInt32(scaledPref));
			}
			else
			{
				finalWidth = holder.requestedSize.Width = Convert.ToInt32(w);
			}

			int r = 0;
			var rowsSpecified = opts.rows != float.MinValue;
			var heightSpecified = opts.height != int.MinValue;
			var hpSpecified = opts.hp != int.MinValue;

			if (hpSpecified)
			{
				finalHeight = lastControl != null ? lcHeight + (int)(opts.hp * dpiscale) : 0;
			}
			else
			{
				if (rowsSpecified)
				{
					r = Convert.ToInt32(opts.rows);
				}
				else if (heightSpecified)
				{
					if (opts.height != -1)
					{
						if (ctrl is KeysharpComboBox cmb && cmb.DropDownStyle != ComboBoxStyle.Simple)
						{
							var combinedHeight = Convert.ToInt32(dpiscale * opts.height);
							var baseHeight = cmb.PreferredSize.Height;
							var minDropHeight = GetComboMinDropHeight(cmb);
							var dropHeight = Math.Max(minDropHeight, combinedHeight - baseHeight);
							cmb.IntegralHeight = false;
							cmb.DropDownHeight = dropHeight;
							finalHeight = baseHeight;
						}
						else
						{
							finalHeight = holder.requestedSize.Height = Convert.ToInt32(dpiscale * opts.height);
						}
					}
				}
				else
				{
					if (ctrl is KeysharpComboBox || ctrl is KeysharpListBox)
						r = 3;
					else if (ctrl is KeysharpListView || ctrl is KeysharpTreeView || (ctrl is KeysharpProgressBar kpb2 && ((kpb2.AddStyle & 0x04) == 0x04)))
						r = 5;
					else if (ctrl is KeysharpProgressBar)
#if WINDOWS
						r = 2;
#else
						r = 1;
#endif
					else if (ctrl is KeysharpGroupBox)
						r = 2;
					else if (ctrl is KeysharpTextBox tb)
						r = tb.Multiline ? 3 : 1;
					else if (ctrl is KeysharpPasswordBox || ctrl is KeysharpDateTimePicker || ctrl is HotkeyBox)
						r = 1;
					else if (ctrl is TabPage || ctrl is KeysharpTabControl)
						r = 10;

#if WINDOWS
					else if (ctrl is KeysharpCustomControl custom)
						r = 5;//AHK used 5.

#endif
				}

				if (rowsSpecified || !heightSpecified)
				{
					var fontRows = (int)(Math.Round(fontpixels + 0.5) * r);//This is a rough attempt to make text boxes tall enough to show the requested number of lines without having the scrollbars appear unnecessarily.
					var defheight = fontRows;//AHK used external leading, but just use fontpixels here because it's close enough.

					if (ctrl is KeysharpComboBox cmb)
					{
						if (rowsSpecified)
						{
							var rowCount = Math.Max(r, 1);
							cmb.MaxDropDownItems = rowCount;
							if (cmb.DropDownStyle != ComboBoxStyle.Simple)
							{
								cmb.IntegralHeight = false;
								cmb.DropDownHeight = GetComboDropHeight(cmb, rowCount);
							}
						}
					}
					else if (ctrl is KeysharpListBox lb)
					{
						var chromeHeight = lb.ClientSize.Height > 1 ? lb.GetSize().Height - lb.ClientSize.Height : lb.ItemHeight;//This is mostly needed on Linux because ClientSize isn't calculated before showing the form.
						finalHeight = lb.ItemHeight * r + chromeHeight + lb.Margin.Bottom;
					}
					else if (ctrl is KeysharpTreeView tv)
					{
#if WINDOWS
						finalHeight = tv.ItemHeight * r - tv.Margin.Bottom;//For some reason, TreeView doesn't appear to need to have DPI scaling applied, and also is a bit too large, so we subtract the margin.
#else
						finalHeight = fontRows;
#endif
					}
					else if (ctrl is KeysharpGroupBox gb)
					{
						finalHeight = defheight + ((gb.Margin.Top + gb.Margin.Bottom) * (2 + ((int)(r + 1.5) - 2)));//This odd formula comes straight from the AHK source.
					}
					else if (ctrl is KeysharpListView lv)
					{
						finalHeight = defheight + lv.Margin.Top + lv.Margin.Bottom;//ListView doesn't have an ItemHeight property, so attempt to compute it here.
					}
					else if (ctrl is KeysharpTabControl tc2)
					{
						finalHeight = defheight + Convert.ToInt32((tc2.Margin.Top + tc2.Margin.Bottom) *  (2.0 + ((int)(r + 1.5) - 1)));//Same here, but -1.
					}

#if WINDOWS
					else if (ctrl is KeysharpCustomControl)
					{
						finalHeight = fontRows + ctrl.Margin.Top;
					}

#endif
					else
					{
						if (!rowsSpecified) //Neither r or h were specified.
						{
								if (ctrl is KeysharpTrackBar trk && opts.thick == int.MinValue)//Separate check for TrackBar because the documentation specifies it in pixels. Skip this if thickness has been specified.
								{
#if WINDOWS
									finalHeight = trk.Orientation == Orientation.Horizontal ? 30 : Convert.ToInt32(5 * fontpixels);
#else
									finalHeight = trk.Orientation == Orientation.Horizontal ? Convert.ToInt32(2.75 * fontpixels) : Convert.ToInt32(5.5 * fontpixels);
#endif
									goto heightdone;
								}
							else if (ctrl is KeysharpLabel lbl)
							{
								bool hasW = !widthAuto && (opts.width != int.MinValue || opts.wp != int.MinValue);
								bool hasH = !heightAuto && (opts.height != int.MinValue || opts.hp != int.MinValue);

								if (hasW && !hasH)
								{
									ctrl.MinimumSize = new Size(finalWidth, 0);
									ctrl.MaximumSize = new Size(finalWidth, int.MaxValue);
									lbl.AutoSize = true;
								}
								else if (!hasW && hasH)
								{
									ctrl.MinimumSize = new Size(0, finalHeight);
									ctrl.MaximumSize = new Size(int.MaxValue, finalHeight);
									lbl.AutoSize = true;
								}
								else if (!hasW && !hasH)
								{
									//No width was specified, so request auto width (-1) rather than pinning the label to
									//its initial (possibly empty) content. SetSize keeps this as the control's
									//UserPreferredSize, so the backend re-measures it to fit the text when it changes.
									finalWidth = -1;
									lbl.AutoSize = true;
								}
								goto heightdone;
							}
						}

						if (r > 0)
						{
							//Total height = the control's chrome (border/non-client) + room for the requested text rows.
							//The chrome is the control's outer height minus its client height, but only once the client
							//area is real. On Linux/OSX, ClientSize reports the still-unallocated 1x1 size before the form
							//is shown while GetSize() falls back to PreferredSize (chrome + one text row), so that term would
							//balloon to almost a full natural height and add a spurious row on top of fontRows. Add no chrome
							//in that case and rely on the PreferredSize clamp below (same guard used for ListBox above).
							var chrome = ctrl.ClientSize.Height > 1 ? ctrl.GetSize().Height - ctrl.ClientSize.Height : 0;
							finalHeight = chrome + fontRows;
#if LINUX
							//Before the form is shown, single-line inputs would otherwise end up far shorter than GTK
							//actually renders them, so the next y+n control overlaps them. Never lay a control out shorter
							//than its natural (preferred) height.
							finalHeight = Math.Max(finalHeight, ctrl.PreferredSize.Height);
#endif
#if OSX
							if (ctrl is KeysharpTextBox { Multiline: false } || ctrl is KeysharpPasswordBox || ctrl is HotkeyBox)
								finalHeight = Math.Max(finalHeight, (int)Math.Ceiling(fontpixels + (8 * dpiscale)));
#endif
						}
						else
						{
							var ctrlheight = ctrl.PreferredSize.Height;
#if WINDOWS
							ctrlheight += ctrl.GetSize().Height - ctrl.ClientSize.Height;//Account for the border.
#endif
							finalHeight = ctrlheight.Ai();
						}
					}
				}
			}

		heightdone:
#if LINUX
			//Several GTK widgets refuse to render below an intrinsic minimum (a single text row plus chrome):
			//entries, spin buttons and date pickers. When a smaller height was requested explicitly - e.g. an
			//UpDown given "h20" - the widget still draws taller, so the next y+n control would land on top of it.
			//Lay such controls out at least as tall as they actually render. Multi-row/variable controls (lists,
			//multiline text, containers) are excluded so an intentionally smaller explicit height is still honored.
			if (finalHeight >= 0
				&& (ctrl is KeysharpNumericUpDown || ctrl is KeysharpTextBox { Multiline: false }
					|| ctrl is KeysharpPasswordBox || ctrl is HotkeyBox || ctrl is KeysharpDateTimePicker))
				finalHeight = Math.Max(finalHeight, ctrl.PreferredSize.Height);
#endif
			ctrl.SetSize(new Size(finalWidth, finalHeight));

			if (finalWidth < 0 || finalHeight < 0)
			{
				var actualSize = ctrl.GetSize();
				if (finalWidth < 0) finalWidth = actualSize.Width;
				if (finalHeight < 0) finalHeight = actualSize.Height;
			}
			Point loc;

			var xoffset = (double)lcLeft;
			var yoffset = (double)lcTop;

			//Absolute (Xn/Yn) and Margin (XM/YM) coordinates are measured from the control's "parent window".
			//For Tab3 sub-controls that parent window is the tab dialog that fills the tab's display area
			//(see the AHK docs, Tab Remarks -> Parent window), not the outer GUI window. So when the control
			//lives inside a tab page, resolve these coordinates against the tab page's content origin rather
			//than the form's top-left; otherwise the tab's content inset (which is larger on Mac/Linux than on
			//Windows) would push such controls outside the tab area. pwx/pwy stay 0 when not inside a tab.
			double pwx = 0.0, pwy = 0.0;

			if (opts.xpos == GuiOptions.Positioning.Absolute || opts.ypos == GuiOptions.Positioning.Absolute
				|| opts.xpos == GuiOptions.Positioning.Margin || opts.ypos == GuiOptions.Positioning.Margin)
			{
				Point p = LastContainer?.GetLocationRelativeToForm() ?? Point.Empty;

				var parentWindow = LastContainer;
				while (parentWindow != null && parentWindow is not TabPage)
					parentWindow = parentWindow.GetLogicalParent();

				if (parentWindow is TabPage)
				{
					Point pw = parentWindow.GetLocationRelativeToForm();
					pwx = pw.X;
					pwy = pw.Y;
				}

				if (opts.xpos == GuiOptions.Positioning.Absolute || opts.xpos == GuiOptions.Positioning.Margin)
					xoffset = p.X;
				if (opts.ypos == GuiOptions.Positioning.Absolute || opts.ypos == GuiOptions.Positioning.Margin)
					yoffset = p.Y;
			}

			if (opts.xpos == GuiOptions.Positioning.Absolute)
				xoffset = (opts.x * dpiscale) + pwx - xoffset;
			else if (opts.xpos == GuiOptions.Positioning.PreviousBottomRight)
				xoffset += lcWidth + (opts.x * dpiscale);
			else if (opts.xpos == GuiOptions.Positioning.PreviousTopLeft)
				xoffset += opts.x * dpiscale;
			else if (opts.xpos == GuiOptions.Positioning.Margin)
				xoffset = form.Margin.Left + (opts.x * dpiscale) + pwx - xoffset;
			else if (opts.xpos == GuiOptions.Positioning.Section)
				xoffset = (section?.X ?? 0) + (opts.x * dpiscale);
			else if (opts.xpos == GuiOptions.Positioning.Container)
				xoffset = opts.x * dpiscale;
			else
				xoffset = int.MinValue;

			if (opts.ypos == GuiOptions.Positioning.Absolute)
				yoffset = (opts.y * dpiscale) + pwy - yoffset;
			else if (opts.ypos == GuiOptions.Positioning.PreviousBottomRight)
				yoffset += lcHeight + (opts.y * dpiscale);
			else if (opts.ypos == GuiOptions.Positioning.PreviousTopLeft)
				yoffset += opts.y * dpiscale;
			else if (opts.ypos == GuiOptions.Positioning.Margin)
				yoffset = form.Margin.Top + (opts.y * dpiscale) + pwy - yoffset;
			else if (opts.ypos == GuiOptions.Positioning.Section)
				yoffset = (section?.Y ?? 0) + (opts.y * dpiscale);
			else if (opts.ypos == GuiOptions.Positioning.Container)
				yoffset = opts.y * dpiscale;
			else
				yoffset = int.MinValue;

			//X specified, but Y wasn't.
			if (xoffset != int.MinValue && yoffset == int.MinValue)
			{
				if (opts.xpos == GuiOptions.Positioning.PreviousTopLeft && opts.x == 0)//XP or XP+0: Below the previous control (bottom edge plus margin).
					yoffset = lcBottom + form.Margin.Top;
				else if (opts.xpos == GuiOptions.Positioning.PreviousBottomRight || opts.xpos == GuiOptions.Positioning.PreviousTopLeft)//X+n or XP+nonzero (Already checked for xp == 0 above): Same as the previous control's top edge (YP).
					yoffset = lcTop;
				else if ((opts.xpos == GuiOptions.Positioning.Absolute || opts.xpos == GuiOptions.Positioning.Container || opts.xpos == GuiOptions.Positioning.Margin) && needsContainerMostForPositioning)//Xn or XM: Beneath all previous controls (maximum Y extent plus margin).
				{
					var (right, bottom) = rbContainerMost;//Get the bottom-most control in the current container.
					var bLoc = bottom?.GetLocation() ?? Point.Empty;
					var bSize = bottom?.GetSize() ?? Size.Empty;
					yoffset = bLoc.Y + bSize.Height + form.Margin.Top;
				}
				else if (opts.xpos == GuiOptions.Positioning.Section && needsSectionMostForPositioning)//XS: Beneath all previous controls since the most recent use of the Section option.
				{
					yoffset = section.Value.Bottom + form.Margin.Top;
				}
			}
			else if (xoffset == int.MinValue && yoffset != int.MinValue)//Y, but not X.
			{
				if (opts.ypos == GuiOptions.Positioning.PreviousTopLeft && opts.y == 0)//YP or YP+0: To the right of the previous control (right edge plus margin)..
					xoffset = lcRight + form.Margin.Left;
				else if (opts.ypos == GuiOptions.Positioning.PreviousBottomRight || opts.ypos == GuiOptions.Positioning.PreviousTopLeft)//Y+n or YP+nonzero (Already checked for yp == 0 above): Same as the previous control's left edge (XP).
					xoffset = lcLeft;
				else if ((opts.ypos == GuiOptions.Positioning.Absolute || opts.ypos == GuiOptions.Positioning.Container || opts.ypos == GuiOptions.Positioning.Margin) && needsContainerMostForPositioning)//Yn or YM: To the right of all previous controls (maximum X extent plus margin).
				{
					var (right, bottom) = rbContainerMost;//Get the right-most control in the current container.
					var rLoc = right?.GetLocation() ?? Point.Empty;
					var rSize = right?.GetSize() ?? Size.Empty;
					xoffset = rLoc.X + rSize.Width + form.Margin.Left;
				}
				else if (opts.ypos == GuiOptions.Positioning.Section && needsSectionMostForPositioning)//YS: To the right of all previous controls since the most recent use of the Section option.
				{
					xoffset = section.Value.Right + form.Margin.Left;
				}
			}
			else if (xoffset == int.MinValue && yoffset == int.MinValue && ctrl is KeysharpNumericUpDown)
			{
				xoffset = lcLeft; yoffset = lcTop;
			}

			loc = new Point(Convert.ToInt32(xoffset), Convert.ToInt32(yoffset));

			//Note we check DockStyle here because if the previous control was docked to a side, then we can't really use its location as a reference to base this control's location off of.
			// Only runs when the added control is itself the status strip (not on every control add); the strip is
			// also re-laid-out on resize and on show via the other UpdateStatusStripLayout call sites, and the method
			// is a no-op on Windows.
			if (ctrl is KeysharpStatusStrip ksss)
			{
				form.UpdateStatusStripLayout();
			}
			else if (loc.X != int.MinValue && loc.Y != int.MinValue)//If both x and y were specified, that takes precedence over everything else.
			{
			}
			else if (lastControl != null && lastControl.Dock == DockStyle.None && loc.X == int.MinValue && loc.Y == int.MinValue)
			{
				var deadspace = 0;
				if (IsTextLike(ctrl) && IsTextLike(lastControl)
					&& lastControl.GetLogicalParent() == ctrl.GetLogicalParent())
				{
					deadspace = Convert.ToInt32(4 * dpiscale);
				}
				loc = new Point(lcLeft, lcTop + lcHeight + form.Margin.Bottom + deadspace);
			}
			else if (lastControl != null && lastControl.Dock == DockStyle.None && loc.X == int.MinValue && needsLastParentMostForPositioning)
			{
				//Will only have gotten here if y was specified in absolute coords using Yn with x omitted.
				var (right, bottom) = lastControlParentMost;//Get the right-most control in the current container.
				var rLoc = right?.GetLocation() ?? Point.Empty;
				var rSize = right?.GetSize() ?? Size.Empty;
				loc = new Point(rLoc.X + rSize.Width + right.Margin.Right, loc.Y);
			}
			else if (lastControl != null && lastControl.Dock == DockStyle.None && loc.Y == int.MinValue && needsLastParentMostForPositioning)//Same but for loc.X.
			{
				//Will only have gotten here if x was specified in absolute coords using Xn with y omitted.
				var (right, bottom) = lastControlParentMost;//Get the bottom-most control in the current container.
				var bLoc = bottom?.GetLocation() ?? Point.Empty;
				var bSize = bottom?.GetSize() ?? Size.Empty;
				loc = new Point(loc.X, bLoc.Y + bSize.Height + bottom.Margin.Bottom);
			}
			else//Final fallback when nothing else has worked.
			{
				var top = (double)prevParent.Margin.Top;

				if (prevParent is Form f && f.MainMenuStrip != null)
				{
#if WINDOWS
					top += f.MainMenuStrip.GetSize().Height;
#endif
				}

				if (loc.Y == int.MinValue && LastContainer is KeysharpGroupBox gblast)
				{
					//Top needs to be manually adjusted when the container is a GroupBox, we're adding the first control, and they haven't explicitly specified a Y coordinate.
					if (lastContainerChildCount == 0)
						top += gblast.Margin.Top + gblast.Padding.Bottom;
				}

				loc = new Point(opts.x != int.MinValue ? opts.x : prevParent.Margin.Left,
										  opts.y != int.MinValue ? opts.y : Convert.ToInt32(top));
			}

			if (isTabControl)
			{
				var ktc = (KeysharpTabControl)ctrl;
				if (ktc.TabPages.Count > 0)
					holder.UseTab(1);//Will set this object's CurrentTab value, as well as the LastContainer values.
				else
					LastContainer = ktc.Parent;

				if (opts.bgtrans)
					ktc.SetColor(Color.Transparent);
				else if (opts.bgcolor.HasValue)
					ktc.SetColor(opts.bgcolor.Value);

				if (prevParent != form)
				{
					var parentSize = prevParent.GetSize();
					var ctrlRight = loc.X + finalWidth;
					var ctrlBottom = loc.Y + finalHeight;
					ctrl.SetSize(new Size(Math.Min(parentSize.Width - (2 * prevParent.Margin.Right), ctrlRight), Math.Min(parentSize.Height - (2 * prevParent.Margin.Top), ctrlBottom)));
				}
			}
			else if (ctrl is KeysharpRadioButton krb)
			{
				if (isNewRadioGroup)
				{
					var panel = radioGroupPanel ?? new Panel();
					var parent = LastContainer;
					//panel.BorderStyle = BorderStyle.FixedSingle;//For debugging so we can see where the panel is.
					if (radioGroupPanel == null)
						parent.TagAndAdd(panel);
					panel.SetLocation(new Point(Math.Max(parent.Margin.Left, loc.X), Math.Max(parent.Margin.Top, loc.Y)));
					loc = new Point(panel.Margin.Left, panel.Margin.Top);
					panel.SetSize(new Size(finalWidth + panel.Margin.Left + panel.Margin.Right, finalHeight + panel.Margin.Top + panel.Margin.Bottom));
#if WINDOWS
					panel.AutoSize = true;
#endif
					if (radioGroupPanel == null)
						panel.TagAndAdd(holder);
					LastContainer = panel;
				}
				else
				{
#if !WINDOWS
					var pnl = LastContainer as Panel ?? LastContainer?.Parent as Panel;
					if (pnl != null)
					{
						var pnlSize = pnl.GetSize();
						var neededWidth = Math.Max(pnlSize.Width, loc.X + finalWidth + pnl.Margin.Right);
						var neededHeight = Math.Max(pnlSize.Height, loc.Y + finalHeight + pnl.Margin.Bottom);
						if (neededWidth != pnlSize.Width || neededHeight != pnlSize.Height)
							pnl.SetSize(new Size(neededWidth, neededHeight));
					}
#endif
				}

				krb.Checked = opts.ischecked.HasValue && opts.ischecked.Value > 0;
			}
			else if (ctrl is KeysharpGroupBox gb)
			{
				if (opts.group)
					LastContainer = gb;
			}

			ctrl.SetLocation(loc);
			holder.dpiResize = opts.dpiresize ?? defaultDpiResize;
			controls[ctrl.Handle.ToInt64()] = holder;
#if !WINDOWS
			Keysharp.Internals.Window.Unix.EtoMessageSource.Attach(ctrl);
#endif

#if WINDOWS
			if (ctrl is KeysharpActiveX kax)
				kax.Init();

#endif

			if (ctrl is KeysharpPictureBox pbox)
			{
				if (textStr != null && ImageHelper.LoadImage(textStr, opts.width, opts.height, opts.iconnumber).Item1 is Bitmap bmp)
				{
					if (pbox.SizeMode == PictureBoxSizeMode.Zoom)
					{
						var ratio = bmp.Height != 0 ? (double)bmp.Width / bmp.Height : 1;

						if (ratio == 0)
							ratio = 1;

						if (opts.width < 0)
						{
							var pboxSize = pbox.GetSize();
							pbox.SetSize(new Size((int)(pboxSize.Height * ratio), pboxSize.Height));
						}
						else
						{
							var pboxSize = pbox.GetSize();
							pbox.SetSize(new Size(pboxSize.Width, (int)(pboxSize.Width / ratio)));
						}
					}

					pbox.Image = bmp;
					pbox.SetSize(bmp.Size);
					//pbox.BackgroundImage = bmp;
				}
			}

			//If this control landed inside a container that auto-sizes a dimension, grow it to fit the content.
			for (var anc = ctrl.GetLogicalParent(); anc != null; anc = anc.GetLogicalParent())
			{
				if (anc is KeysharpGroupBox)
				{
					FitContainerToContent(anc);
					break;
				}
			}

			if (form.BeenShown)//See above for a description of this logic.
			{
				//If it's been shown and is contained anywhere within the hierarchy of a TabControl, then resize
				//every TabControl in the chain.
				foreach (var partc in ctrl.FindParents<KeysharpTabControl>())
					partc.AdjustSize(dpiscale, new Size(int.MinValue, int.MinValue));
			}

			//The Section option starts a new section, as does the first control in a container; later ones
			//only widen the extents XS/YS measure against - the corner never moves. Updated after positioning
			//so a control carrying both Section and XS/YS uses the previous section and starts the new one.
			//A status bar takes no part, matching AHK's carve-out.
			if (ctrl is not KeysharpStatusStrip && placementContainer != null)
			{
				var bounds = new Rectangle(ctrl.GetLocation(), ctrl.GetSize());

				if (!opts.section && sections.TryGetValue(placementContainer, out var grown))
				{
					grown.Width = Math.Max(grown.Width, bounds.Right - grown.X);
					grown.Height = Math.Max(grown.Height, bounds.Bottom - grown.Y);
					sections[placementContainer] = grown;
				}
				else
					sections[placementContainer] = bounds;
			}

			//Applied only once the control has been measured and placed: a hidden widget measures as zero, so
			//hiding it any earlier collapses it to 0x0 and takes every sibling positioned against it along too.
			if (opts.visible.HasValue)
				ctrl.Visible = opts.visible.Value;

#if !WINDOWS
			if (form.Visible)
			{
				form.Invalidate();
				form.UpdateLayout();
				form.Show();
			}
#endif

			return holder;

			int GetComboItemHeight(KeysharpComboBox combo)
			{
#if WINDOWS
				return combo.ItemHeight > 0 ? combo.ItemHeight : Convert.ToInt32(fontpixels + 0.5);
#else
				return Convert.ToInt32(fontpixels + 0.5);
#endif
			}

			int GetComboDropChromeHeight()
			{
#if WINDOWS
				return SystemInformation.BorderSize.Height * 2;
#else
				return 2;
#endif
			}

			int GetComboMinDropHeight(KeysharpComboBox combo)
			{
				var itemHeight = GetComboItemHeight(combo);
				var chrome = GetComboDropChromeHeight();
				return itemHeight + chrome;
			}

			int GetComboDropHeight(KeysharpComboBox combo, int rows)
			{
				rows = Math.Max(rows, 1);
				var itemHeight = GetComboItemHeight(combo);
				var chrome = GetComboDropChromeHeight();
				var height = (itemHeight * rows) + chrome;
				var minHeight = itemHeight + chrome;
				return Math.Max(height, minHeight);
			}
			bool IsTextLike(Forms.Control control) => control is KeysharpLabel || control is KeysharpLinkLabel;
		}

		public object AddActiveX(object options = null, object text = null) => Add(Keyword_ActiveX, options, text);

		public object AddButton(object options = null, object text = null) => Add(Keyword_Button, options, text);

		public object AddCheckbox(object options = null, object text = null) => Add(Keyword_CheckBox, options, text);

		public object AddComboBox(object options = null, object text = null) => Add(Keyword_ComboBox, options, text);

		public object AddCustom(object options = null, object text = null) => Add(Keyword_Custom, options, text);

		public object AddDateTime(object options = null, object text = null) => Add(Keyword_DateTime, options, text);

		public object AddDDL(object options = null, object text = null) => Add(Keyword_DropDownList, options, text);

		public object AddDropDownList(object options = null, object text = null) => Add(Keyword_DropDownList, options, text);

		public object AddEdit(object options = null, object text = null) => Add(Keyword_Edit, options, text);

		public object AddGroupBox(object options = null, object text = null) => Add(Keyword_GroupBox, options, text);

		public object AddHotKey(object options = null, object text = null) => Add(Keyword_Hotkey, options, text);

		public object AddLink(object options = null, object text = null) => Add(Keyword_Link, options, text);

		public object AddListBox(object options = null, object text = null) => Add(Keyword_ListBox, options, text);

		public object AddListView(object options = null, object text = null) => Add(Keyword_ListView, options, text);

		public object AddMonthCal(object options = null, object text = null) => Add(Keyword_MonthCal, options, text);

		public object AddPic(object options = null, object text = null) => Add(Keyword_Picture, options, text);

		public object AddPicture(object options = null, object text = null) => Add(Keyword_Picture, options, text);

		public object AddProgress(object options = null, object text = null) => Add(Keyword_Progress, options, text);

		public object AddRadio(object options = null, object text = null) => Add(Keyword_Radio, options, text);

		public object AddSlider(object options = null, object text = null) => Add(Keyword_Slider, options, text);

		public object AddStatusBar(object options = null, object text = null) => Add(Keyword_StatusBar, options, text);

		public object AddTab(object options = null, object text = null) => Add(Keyword_Tab, options, text);
		// Just for compatibility
		public object AddTab2(object options = null, object text = null) => Add(Keyword_Tab, options, text);
		public object AddTab3(object options = null, object text = null) => Add(Keyword_Tab, options, text);

		public object AddText(object options = null, object text = null) => Add(Keyword_Text, options, text);

		public object AddTreeView(object options = null, object text = null) => Add(Keyword_TreeView, options, text);

		public object AddUpDown(object options = null, object text = null) => Add(Keyword_UpDown, options, text);

		public object AddWebBrowser(object options = null, object text = null) => Add(Keyword_WebBrowser, options, text);

		public object Destroy()
		{
#if LINUX
			// Drop any cached Wayland self-position correlation so a recycled native handle can't
			// inherit this window's compositor id. IsSupported first, to avoid form.Handle on X11.
			if (form != null && Keysharp.Internals.Window.Linux.Wayland.WaylandOwnToplevels.IsSupported)
				Keysharp.Internals.Window.Linux.Wayland.WaylandOwnToplevels.Forget(form.Handle);
#endif
			return form?.Destroy();
		}

		public object Flash(object blink)
		{
#if WINDOWS
			_ = WindowsAPI.FlashWindow(form.Handle, blink.Ab(true));
#endif
			return DefaultObject;
		}

		public object GetClientPos([Optional()][DefaultParameterValue(null)] object x,
								   [Optional()][DefaultParameterValue(null)] object y,
								   [Optional()][DefaultParameterValue(null)] object width,
								   [Optional()][DefaultParameterValue(null)] object height)
		{
			Gui.Control.GetClientPos(form, DpiScale, x, y, width, height);
			return DefaultObject;
		}

		IEnumerator<(object, object)> IEnumerable<(object, object)>.GetEnumerator() => CreateEnumerator(2);

		public object GetPos([Optional()][DefaultParameterValue(null)] object x, [Optional()][DefaultParameterValue(null)] object y, [Optional()][DefaultParameterValue(null)] object width, [Optional()][DefaultParameterValue(null)] object height)
		{
			Gui.Control.GetPos(form, DpiScale, x, y, width, height);
			return DefaultObject;
		}

		public object Hide()
		{
			form.Hide();
			return DefaultObject;
		}

		public object Maximize()
		{
			var result = form.WindowState = FormWindowState.Maximized;
			ReassertWindowStateOnWayland(FormWindowState.Maximized);
			return result;
		}

		public object Minimize()
		{
			var result = form.WindowState = FormWindowState.Minimized;
			ReassertWindowStateOnWayland(FormWindowState.Minimized);
			return result;
		}

		// On Wayland, Eto's WindowState setter (a GTK xdg-toplevel request) is the primary path, but some
		// compositors drop a client's state request; reassert it through the compositor backend, correlating
		// our own window the same way Gui.Show does. No-op on X11/other platforms. IsSupported (a cheap
		// Wayland-session check) is tested before form.Handle so X11 doesn't make a native xid call.
		private void ReassertWindowStateOnWayland(FormWindowState state)
		{
#if LINUX
			if (!Keysharp.Internals.Window.Linux.Wayland.WaylandOwnToplevels.IsSupported)
				return;

			var sz = form.Size;
			Keysharp.Internals.Window.Linux.Wayland.WaylandOwnToplevels.SetWindowState(
				form, form.Title, sz.Width, sz.Height, state);
#endif
		}

		public object Move(object x = null, object y = null, object width = null, object height = null)
		{
			var xVal = (x is null ? int.MinValue : x.ToInt());
			var yVal = (y is null ? int.MinValue : y.ToInt());
			var widthVal = (width is null ? int.MinValue : width.ToInt());
			var heightVal = (height is null ? int.MinValue : height.ToInt());
			var scale = DpiScale;
			var formLoc = form.GetLocation();
			var formSize = form.GetSize();

			if (xVal != int.MinValue)
				formLoc.X = xVal;

			if (yVal != int.MinValue)
				formLoc.Y = yVal;

			if (widthVal != int.MinValue)
				formSize.Width = Convert.ToInt32(widthVal * scale);

			if (heightVal != int.MinValue)
				formSize.Height = Convert.ToInt32(heightVal * scale);

			if (xVal != int.MinValue || yVal != int.MinValue)
				form.SetLocation(formLoc);
			if (widthVal != int.MinValue || heightVal != int.MinValue)
				form.SetSize(formSize);

#if LINUX
			// Wayland: SetLocation above can't move our own window, so drive the compositor backend.
			// IsSupported is checked first to avoid evaluating form.Handle on X11.
			if ((xVal != int.MinValue || yVal != int.MinValue)
					&& Keysharp.Internals.Window.Linux.Wayland.WaylandOwnToplevels.IsSupported)
				Keysharp.Internals.Window.Linux.Wayland.WaylandOwnToplevels.Position(form, form.Title, xVal, yVal, formSize.Width, formSize.Height);
#endif
			return DefaultObject;
		}

		public object OnEvent(object eventName, object callback, object addRemove = null) => form.OnEvent(eventName, callback, addRemove);

		/// <summary>
		/// Registers a function to be called when the GUI window receives the specified window message.
		/// </summary>
		/// <param name="msgNumber">The number of the message to monitor.</param>
		/// <param name="callback">The function to call, as <c>Callback(GuiObj, wParam, lParam, Msg)</c>.
		/// If it returns a non-zero value, that value becomes the message's result and neither the remaining
		/// handlers nor the default window procedure run.</param>
		/// <param name="addRemove">If omitted, defaults to 1. Otherwise 1 to call this callback after any
		/// previously registered ones, -1 to call it before them, or 0 to unregister it.</param>
		/// <remarks>
		/// Off Windows there is no native message queue, so only the input messages EtoMessageSource
		/// synthesizes from the toolkit's own events are ever delivered here; see OnMessage() for that set.
		/// </remarks>
		public object OnMessage(object msgNumber, object callback, object addRemove = null)
		{
			var addremove = addRemove.Al(1L);

			//AHK's GuiType::OnEvent rejects anything outside -1..1: a GUI event can only run one thread at a
			//time, so the parameter is an ordering/removal switch rather than a thread count.
			if (addremove < -1 || addremove > 1)
				return Errors.ValueErrorOccurred($"Invalid AddRemove value: {addremove}.");

			var del = Functions.GetKeysharpFunc(callback, form.eventObj, true);

			if (del == null)
				return Errors.ValueErrorOccurred("The callback was not a valid function.");

			var msg = (int)msgNumber.Al();
			messageHandlers ??= new();
			_ = messageHandlers.GetOrAdd(msg, static _ => new()).ModifyEventHandlers(del, addremove);
#if !WINDOWS

			//Pointer motion is only watched for while something is listening; see SyncMotionHooks().
			if (msg == Keysharp.Internals.Os.Windows.WindowsAPI.WM_MOUSEMOVE)
				Keysharp.Internals.Window.Unix.EtoMessageSource.SyncMotionHooks();

#endif
			return DefaultObject;
		}

		//Keyed by window message number, populated by OnMessage() and drained from KeysharpForm.WndProc
		//on Windows and from EtoMessageSource elsewhere.
		private ConcurrentDictionary<int, CallbackHub> messageHandlers;

		/// <summary>
		/// Whether this GUI has an OnMessage() handler for the given message. Lets the off-Windows message
		/// source decide whether a toolkit event is worth subscribing to at all.
		/// </summary>
		internal bool HasWindowMessageHandler(int msg)
			=> messageHandlers != null && messageHandlers.TryGetValue(msg, out var handler) && handler?.IsEmpty == false;

		/// <summary>
		/// Runs any OnMessage() handlers registered for this message, ahead of the default window procedure
		/// (mirroring AHK's GuiWindowProc, which consults its message monitors before DefDlgProc).
		/// </summary>
		/// <returns>True if a handler claimed the message and supplied its result.</returns>
		internal bool InvokeWindowMessageHandlers(ref Message m)
		{
			if (messageHandlers == null || !messageHandlers.TryGetValue(m.Msg, out var handler))
				return false;

			var result = handler?.InvokeWindowMessageHandlers(this, m.WParam.ToInt64(), m.LParam.ToInt64(), (long)m.Msg);

			//"Claimed" must be the same test the chain broke on, so reuse that predicate rather than restating
			//it: a NON-EMPTY return claims the message (an explicit 0 replies 0 and suppresses default
			//processing), while "" or no return at all falls through.
			if (!CallbackStop.NonEmpty(result))
				return false;

			m.Result = (nint)result.Al();
			return true;
		}

		public object Opt(object options)
		{
			var optionsVal = options.As();

			foreach (var split in Options.ParseOptions(optionsVal))
			{
				if (split.Length == 0)
					continue;

				// An option with no leading +/- means + (add/enable), per AHK. Separate the sign first; otherwise
				// Substring(1) strips the first letter of a no-sign option (e.g. "AlwaysOnTop", "Owner") and it is misparsed.
				var signed = split[0] == '+' || split[0] == '-';
				var add = split[0] != '-';
				var str = signed ? split.Substring(1) : split;

				if (str.Length > 0)
				{
					if (str.StartsWith("MinSize", StringComparison.OrdinalIgnoreCase))
					{
						if (showOptionsDkt.TryGetValue("MinSize", out var func))
							func(this, (add, add ? str.Substring(7) : ""));
					}
					else if (str.StartsWith("MaxSize", StringComparison.OrdinalIgnoreCase))
					{
						if (showOptionsDkt.TryGetValue("MaxSize", out var func))
							func(this, (add, add ? str.Substring(7) : ""));
					}
					else if (str.StartsWith("Owner", StringComparison.OrdinalIgnoreCase))
					{
						// +Owner / +OwnerHWND adds an owner (a value of "" means owned by the script's main window).
						if (add && showOptionsDkt.TryGetValue("Owner", out var func))
							func(this, str.Substring(5));
					}
					else if (str.StartsWith("Parent", StringComparison.OrdinalIgnoreCase))
					{
						if (add && showOptionsDkt.TryGetValue("Owner", out var func))
							func(this, str.Substring(6));
					}
					else if (showOptionsDkt.TryGetValue(str, out var func))
					{
						func(this, add);
					}
					else if (!IsStyleNumberOption(str))//Raw style numbers (e.g. +0x40000, +E0x8) are applied at construction by the static Opt overload; reject anything else like AHK instead of silently ignoring it.
					{
						return Errors.ValueErrorOccurred("Invalid option.", split);
					}
				}
			}

			return DefaultObject;
		}

		// A bare number ("0x40000"/"262144") is a window style and a leading 'E' + number ("E0x8") an extended
		// style; both are real Win32 style bits, applied at construction by the static Opt(...) overload. AHK
		// validates these (extended style, then pure number) before rejecting any other token as an invalid option.
		private static bool IsStyleNumberOption(string str)
		{
			var span = str.AsSpan();

			if (span.Length > 1 && (span[0] is 'E' or 'e'))
				span = span[1..];

			if (span.IsEmpty)
				return false;

			if (span.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
				return long.TryParse(span[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _);

			return long.TryParse(span, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
		}

		// Implements AutoHotkey's +/-MinSize and +/-MaxSize Gui options (isMin selects which limit):
		//   "+MinSize"/"+MaxSize" with no dimensions -> use the window's current size as the limit; if the window
		//       has not been shown yet there is no real size, so defer to the size at the first Gui.Show.
		//   "-MinSize"/"-MaxSize" -> remove the limit.
		//   "+MinSize<w>x<h>" / "<w>x" / "x<h>" -> set the given DPI-scaled dimensions, leaving an omitted side as-is.
		internal void SetSizeLimit(bool isMin, bool add, string spec)
		{
			// Compute the new limit (null = no change) and whether the limit should be pinned to the size at the
			// first show, then apply both to the relevant property/field in a single branch below.
			Size? limit = null;
			var pinToShow = false;

			if (!add)//"-MinSize"/"-MaxSize": clear the limit.
			{
				limit = NoSizeLimit;
			}
			else if (spec.Length == 0)//"+MinSize"/"+MaxSize": pin to the current size, or defer to the first show.
			{
				if (form.BeenShown)
					limit = form.GetSize();
				else
					pinToShow = true;
			}
			else//Explicit dimensions. DpiScale already folds in the dpiscaling flag (1.0 when off).
			{
				var current = isMin ? form.MinimumSize : form.MaximumSize;

				if (spec.EndsWith("x", StringComparison.OrdinalIgnoreCase))//Only width was specified.
				{
					if (int.TryParse(spec.AsSpan(0, spec.Length - 1), out var width))
						limit = new Size((int)(DpiScale * width), current.Height);
				}
				else if (spec.StartsWith("x", StringComparison.OrdinalIgnoreCase))//Only height was specified.
				{
					if (int.TryParse(spec.AsSpan(1), out var height))
						limit = new Size(current.Width, (int)(DpiScale * height));
				}
				else
				{
					var splits = spec.Split('x', StringSplitOptions.RemoveEmptyEntries);

					if (splits.Length == 2 && int.TryParse(splits[0], out var width) && int.TryParse(splits[1], out var height))
						limit = new Size((int)(DpiScale * width), (int)(DpiScale * height));
				}
			}

			if (isMin)
			{
				pinMinSizeToShowSize = pinToShow;

				if (limit is Size min)
					form.MinimumSize = min;
			}
			else
			{
				pinMaxSizeToShowSize = pinToShow;

				if (limit is Size max)
					form.MaximumSize = max;
			}
		}

		// Resolve any deferred "+MinSize"/"+MaxSize" (no dimensions) to the window's size once it is known (first show).
		private void ApplyDeferredSizeLimits()
		{
			if (pinMinSizeToShowSize)
			{
				pinMinSizeToShowSize = false;
				form.MinimumSize = form.GetSize();
			}

			if (pinMaxSizeToShowSize)
			{
				pinMaxSizeToShowSize = false;
				form.MaximumSize = form.GetSize();
			}
		}

#if WINDOWS
		//Scales a stored pixel measurement from oldDpi to newDpi, rounding the way Win32's MulDiv does.
		private static int ScaleForDpi(int value, int oldDpi, int newDpi) => (int)(((long)value * newDpi + oldDpi / 2) / oldDpi);

		private static Size ScaleForDpi(Size size, int oldDpi, int newDpi) =>
			new (ScaleForDpi(size.Width, oldDpi, newDpi), ScaleForDpi(size.Height, oldDpi, newDpi));

		/// <summary>
		/// Re-lays out the GUI after its DPI changed, mirroring AHK's GuiType::RescaleForDPI.
		/// WinForms' own AutoScaleMode is left at None (see <see cref="KeysharpForm"/>) because it rescales every
		/// child unconditionally, which cannot honor a per-control "-DPIResize".
		/// Fonts are deliberately left alone: AHK stores them as pixel heights and must rescale them, whereas the
		/// fonts used here are point-based and already grow with the device DPI, so scaling would double-apply.
		/// </summary>
		/// <param name="oldDpi">The DPI the current layout was computed for.</param>
		/// <param name="newDpi">The DPI to lay the GUI out for.</param>
		internal void RescaleForDpi(int oldDpi, int newDpi)
		{
			if (form == null || oldDpi <= 0 || newDpi <= 0 || oldDpi == newDpi)
				return;

			//Margins and size limits must be adjusted before anything is resized against them.
			var margin = form.Margin;
			form.Margin = new Padding(ScaleForDpi(margin.Left, oldDpi, newDpi), ScaleForDpi(margin.Top, oldDpi, newDpi),
									  ScaleForDpi(margin.Right, oldDpi, newDpi), ScaleForDpi(margin.Bottom, oldDpi, newDpi));
			form.MinimumSize = ScaleForDpi(form.MinimumSize, oldDpi, newDpi);
			form.MaximumSize = ScaleForDpi(form.MaximumSize, oldDpi, newDpi);
			form.SuspendLayout();

			//Parents are scaled before their children so a child is never momentarily clipped to a container
			//that is still sized for the old DPI.
			foreach (Forms.Control child in form.Controls)
				RescaleControlForDpi(child, oldDpi, newDpi);

			form.ResumeLayout(true);
			//Every control moved, so a partial repaint tends to leave artifacts behind.
			form.Invalidate(true);
		}

		private static void RescaleControlForDpi(Forms.Control ctrl, int oldDpi, int newDpi)
		{
			//Controls Keysharp adds for its own layout (radio group panels, tab pages) have no holder and always
			//follow the window; only script-visible controls can opt out with "-DPIResize".
			var holder = ctrl.GetGuiControl();

			if (holder == null || holder.dpiResize)
			{
				var bounds = ctrl.Bounds;
				//Width and height are scaled from the original extents rather than from the already-rounded
				//position so that repeated transitions don't accumulate drift.
				ctrl.Bounds = new Rectangle(ScaleForDpi(bounds.X, oldDpi, newDpi), ScaleForDpi(bounds.Y, oldDpi, newDpi),
											ScaleForDpi(bounds.Width, oldDpi, newDpi), ScaleForDpi(bounds.Height, oldDpi, newDpi));

				//Kept in step with the bounds because it is stored in pixels and is what Tab controls re-apply.
				//Either dimension may still be int.MinValue ("not specified"), which must be left alone.
				if (holder != null)
				{
					if (holder.requestedSize.Width != int.MinValue)
						holder.requestedSize.Width = ScaleForDpi(holder.requestedSize.Width, oldDpi, newDpi);

					if (holder.requestedSize.Height != int.MinValue)
						holder.requestedSize.Height = ScaleForDpi(holder.requestedSize.Height, oldDpi, newDpi);
				}

				if (ctrl is Forms.ListView lv)
				{
					foreach (ColumnHeader col in lv.Columns)
						col.Width = ScaleForDpi(col.Width, oldDpi, newDpi);
				}
				else if (ctrl is Forms.ListBox lb && lb.IntegralHeight)
				{
					//An integral-height list box silently shrinks to a whole number of items, so a few pixels of
					//rounding loss cost a whole item and enough transitions would shrink it away entirely.
					var itemHeight = lb.ItemHeight;

					if (itemHeight > 0 && ScaleForDpi(bounds.Height, oldDpi, newDpi) - lb.Height > itemHeight / 2)
						lb.Height += itemHeight;
				}
			}

			foreach (Forms.Control child in ctrl.Controls)
				RescaleControlForDpi(child, oldDpi, newDpi);
		}
#endif

		public object Restore()
		{
			if (!form.Visible)
				form.Show();
			form.WindowState = FormWindowState.Normal;
			ReassertWindowStateOnWayland(FormWindowState.Normal);
			return DefaultObject;
		}

		public object SetFont(object options = null, object fontName = null)
		{
			form.SetFont(options, fontName);
			return DefaultObject;
		}

		public object Show(object options = null)
		{
			EnsureDefaultMargins();
			var s = options.As();
			bool /*center = false, cX = false, cY = false,*/ auto = false, min = false, max = false, restore = true, hide = false, cX = false, cY = false;
			var dpiscale = DpiScale;

			foreach (Range r in s.AsSpan().SplitAny(Spaces))
			{
				var opt = s.AsSpan(r).Trim();

				if (opt.Length > 0)
				{
					var mode0 = char.ToLower(opt[0]);
					var select = -1;

					switch (mode0)
					{
						case 'w': select = 0; break;

						case 'h':
						{
							//Make sure starting with 'h' isn't confused for "hide".
							if (!opt.Equals(Keyword_Hide, StringComparison.OrdinalIgnoreCase))
								select = 1; break;
						}

						case 'x': select = 2; break;

						case 'y': select = 3; break;
					}

					if (select == -1)
					{
						switch (opt)
						{
							case var b when opt.Equals(Keyword_AutoSize, StringComparison.OrdinalIgnoreCase):
								auto = true;
								break;

							case var b when opt.Equals(Keyword_Maximize, StringComparison.OrdinalIgnoreCase):
								max = true;
								break;

							case var b when opt.Equals(Keyword_Minimize, StringComparison.OrdinalIgnoreCase):
								min = true;
								break;

							case var b when opt.Equals(Keyword_Restore, StringComparison.OrdinalIgnoreCase):
								form.showWithoutActivation = false;
								restore = true;
								break;

							case var b when opt.Equals(Keyword_NoActivate, StringComparison.OrdinalIgnoreCase):
								form.showWithoutActivation = true;
								restore = true;
								break;
							case var b2 when opt.Equals(Keyword_NA, StringComparison.OrdinalIgnoreCase):
								form.showWithoutActivation = true;
								restore = false;
								break;

							case var b when opt.Equals(Keyword_Hide, StringComparison.OrdinalIgnoreCase):
								hide = true;
								restore = false;
								break;

							case var b when opt.Equals(Keyword_Center, StringComparison.OrdinalIgnoreCase):
								cX = cY = true;
								break;
						}
					}
					else
					{
						var modeval = opt.Slice(1);

						if (modeval.Equals(Keyword_Center, StringComparison.OrdinalIgnoreCase))
						{
							if (select == 2)
							  cX = true;
							else
							  cY = true;
						}
						else if (modeval.Length != 0 && int.TryParse(modeval, out var n))
						{
							switch (select)
							{
								case 0: requestedSize.Width = n; break;
								case 1: requestedSize.Height = n; break;
								case 2: requestedLocation.X = n; break;
								case 3: requestedLocation.Y = n; break;
							}
						}
					}
				}
			}

			ResizeTabControls();
			(int, int) FixStatusStrip(KeysharpStatusStrip ss)
			{
				var maxx = 0;
				var maxy = 0;

				foreach (Forms.Control ctrl in form.GetControls())
				{
					if (ctrl != ss)
					{
						var ctrlLoc = ctrl.GetLocation();
						var ctrlSize = ctrl.GetSize();
						var yval = ctrlLoc.Y + ctrlSize.Height;

						if (yval > maxy)
							maxy = yval;

						var xval = ctrlLoc.X + ctrlSize.Width;

						if (xval > maxx)
							maxx = xval;
					}
				}

				return (maxx, maxy);
			}
			int maxx = 0, maxy = 0, ssHeight = 0;

			if (auto || requestedSize.Width == int.MinValue || requestedSize.Height == int.MinValue)
			{
				// Only the autosize path needs the status strip; computing it unconditionally allocated a status-strip
				// array on every Show, including hot repeated repositions that pass an explicit size.
				var status = form.GetControls().OfType<KeysharpStatusStrip>().ToArray();
				KeysharpStatusStrip ss = null;

				if (status.Length > 0)
				{
					ss = status[0];
					ssHeight = ss.GetSize().Height;
				}

				(maxx, maxy) = FixStatusStrip(ss);
			}

			Size size = Size.Empty;
			if (auto || (!form.BeenShown && !showCalled && requestedSize.Width == int.MinValue && requestedSize.Height == int.MinValue))//The calculations in this block are not exact, but are as close as we can possibly get in a generic way.
			{
				//AHK always autosizes on first show when no dimensions are specified.
				size = new Size(maxx + form.Margin.Left,
										   maxy + ssHeight + form.Margin.Bottom);//Need to manually include the height of the status strip when it's docked.
			}
			else
			{
				size = (form.BeenShown || showCalled) ? form.GetSize() : new Size(800, 500);//Using this size because PreferredSize is so small it just shows the title bar.

				if (requestedSize.Width != int.MinValue)
					size.Width = (int)Math.Ceiling(requestedSize.Width * dpiscale);
				else
					size.Width = (int)(maxx + MarginX);

				if (requestedSize.Height != int.MinValue)
					size.Height = (int)Math.Ceiling(requestedSize.Height * dpiscale);
				else
					size.Height = (int)(maxy + ssHeight + MarginY);

#if !WINDOWS
				form.ClientSize = size;
#endif
			}

#if WINDOWS
			// Single ClientSize assignment for both the autosize and explicit-size branches. The explicit branch used
			// to set it again above, resizing the form twice (two SetWindowPos round-trips) on every Show.
			form.ClientSize = size;
			var screen = Forms.Screen.PrimaryScreen.Bounds;
#else
			form.Content.ClientSize = size;
			form.Properties["AssignedSize"] = size;
			RectangleF screen;
			try { screen = Forms.Screen.PrimaryScreen.Bounds; }
			catch { screen = Forms.Screen.DisplayBounds; }
#endif

			var location = form.BeenShown ? form.GetLocation() : new Point();
			//We need to check showCalled because the user could have called Show("hide")
			//Then called WinMove()
			//Then called Show() again to actually show the window.
			//So don't set the location if it wasn't specified and Show() has already been called once.
			//Same above with size.
			var firstShow = !showCalled && !form.BeenShown;

			var formSize = form.GetSize();
#if LINUX
			// Public X11 coordinates are root pixels, while Eto/GTK positions its own forms in toolkit units. Convert
			// both operands used by centring/explicit placement into the public space, then convert only at SetLocation.
			if (!IsWaylandSession)
			{
				var nativeScreen = Keysharp.Internals.Window.Linux.X11.X11DisplayTopology.FromToolkitBounds(
					new Keysharp.Internals.ScreenRect((int)Math.Round(screen.X), (int)Math.Round(screen.Y),
						(int)Math.Round(screen.Width), (int)Math.Round(screen.Height)));
				screen = nativeScreen.ToRectangle();
				var nativeForm = Keysharp.Internals.Window.Linux.X11.X11DisplayTopology.FromToolkitBounds(
					new Keysharp.Internals.ScreenRect(location.X, location.Y, Math.Max(1, formSize.Width), Math.Max(1, formSize.Height)));
				location = new Point(nativeForm.X, nativeForm.Y);
				formSize = new Size(nativeForm.Width, nativeForm.Height);
			}
#endif
			int centerX = (((int)screen.Width - formSize.Width) / 2) + (int)screen.X;
			int centerY = (((int)screen.Height - formSize.Height) / 2) + (int)screen.Y;

			if (cX) requestedLocation.X = centerX;
			if (cY) requestedLocation.Y = centerY;

			//Strangely, the position does not need to be scaled by DPI.
			if (requestedLocation.X != int.MinValue) location.X = requestedLocation.X;
			else if (firstShow) location.X = centerX;

			if (requestedLocation.Y != int.MinValue) location.Y = requestedLocation.Y;
			else if (firstShow) location.Y = centerY;

			if (!form.BeenShown && (requestedSize.Width == int.MinValue || requestedSize.Height == int.MinValue))
			{
				var currentSize = form.GetSize();
				if (requestedSize.Width == int.MinValue) requestedSize.Width = (int)Math.Ceiling(currentSize.Width / dpiscale);
				if (requestedSize.Height == int.MinValue) requestedSize.Height = (int)Math.Ceiling(currentSize.Height / dpiscale);
			}

			showCalled = true;
			form.StartPosition = FormStartPosition.Manual;

			if (firstShow || requestedLocation.X != int.MinValue || requestedLocation.Y != int.MinValue)
			{
#if LINUX
				if (!IsWaylandSession)
				{
					var toolkit = Keysharp.Internals.Window.Linux.X11.X11DisplayTopology.ToToolkitBounds(
						new Keysharp.Internals.ScreenRect(location.X, location.Y, Math.Max(1, formSize.Width), Math.Max(1, formSize.Height)));
					form.SetLocation(new Point(toolkit.X, toolkit.Y));
				}
				else
#endif
					form.SetLocation(location);
			}

#if !WINDOWS
			// Give menu-less GUIs the standard editing shortcuts on macOS (no-op on Linux), unless -AppMenu
			// was used. GUIs that set their own menu get the Edit menu merged in by Eto's CreateSystemMenu.
			if (firstShow && includeAppMenu)
				GuiHelper.EnsureSystemMenu(form);
#endif

#if LINUX

			//Claim the window with the compositor BEFORE it exists, so the placement that follows can address it
			//by name instead of searching for it. A window is unrecognisable at the moment it is created - no
			//title, no app_id, no geometry - so this is the only point at which the claim can be made.
			//Only when this Show will actually map a new toplevel: a Show of an already-visible window maps
			//nothing, and its reservation would sit armed until some unrelated window of ours consumed it.
			//Must stay immediately before form.Show() with no pump point between - FIFO pairing depends on
			//it; see ReserveWindow.
			if (!hide && !form.Visible)
			{
				var hasPlacement = requestedLocation.X != int.MinValue || requestedLocation.Y != int.MinValue;
				_ = Keysharp.Internals.Window.Linux.Wayland.WaylandOwnToplevels.ReserveWindow(form,
					hasPlacement ? location.X : int.MinValue, hasPlacement ? location.Y : int.MinValue);
			}

#endif

			if (hide)
				form.Hide();
#if WINDOWS
			else if (!form.BeenShown && owner != 0)
			{
				// Form.Show(IWin32Window) only reads owner.Handle, so a plain handle wrapper suffices —
				// NativeWindow.AssignHandle would subclass the owner's wndproc, which fails (Win32Exception)
				// for a window owned by another process, and +Owner explicitly supports foreign owners.
				form.Show(new OwnerHandle(owner));
				form.beenShown = true;
			}
#endif
			else
				form.Show();

			if (min)
				form.WindowState = FormWindowState.Minimized;
			else if (max)
				form.WindowState = FormWindowState.Maximized;
			else if (restore)
				form.WindowState = FormWindowState.Normal;

			// ensure activation on default show/restore (not NA/NoActivate/Hide/Minimize)
			if (!hide && !form.showWithoutActivation && form.WindowState != FormWindowState.Minimized)
			{
				form.Activate();
				form.BringToFront();
			}
#if LINUX
			// Wayland: a client cannot set its own top-level position (form.SetLocation above is a no-op
			// there), so once the window is mapped, ask the compositor backend to place it. We only position
			// when an X/Y/Center was explicitly requested (a plain Show is left to the compositor's own
			// placement), and we reassert here every other window trait GTK cannot express on Wayland. No-op on
			// X11 and on compositors that can't move/decorate windows.
			// IsSupported (a cheap Wayland-session check) is tested first so we don't evaluate form.Handle on
			// X11 — there it triggers a native gdk_x11_window_get_xid call.
			if (!hide && Keysharp.Internals.Window.Linux.Wayland.WaylandOwnToplevels.IsSupported)
			{
				var hasPos = requestedLocation.X != int.MinValue || requestedLocation.Y != int.MinValue;
				// A -Caption window normally loses its titlebar via GTK (the empty-titlebar CSD trick in Eto's
				// GtkWindow), which only works before the window is realized. A window made borderless after that
				// is still server-decorated, so ask the compositor to undecorate it.
				var removeBorder = !caption;
				// Eto's +AlwaysOnTop maps to gtk keep-above, which is a no-op on Wayland, so reassert it via
				// the compositor backend.
				var keepAbove = form.TopMost;
				// Likewise gtk_window_set_skip_taskbar_hint: X11-only, so +ToolWindow needs the compositor.
				var skipTaskbar = !form.ShowInTaskbar;

				//Unconditional because a Show is also a RE-show: hiding the window unmapped it and the compositor
				//dropped everything it had been told, including a transparency this Show doesn't ask for itself.
				//The positioner reconciles the whole desired state, and returns straight away for a window that
				//has none.
				Keysharp.Internals.Window.Linux.Wayland.WaylandOwnToplevels.Position(form, form.Title,
						hasPos ? location.X : int.MinValue, hasPos ? location.Y : int.MinValue,
						size.Width, size.Height, removeBorder, keepAbove, skipTaskbar);
			}
#endif
#if WINDOWS
			form.Update();//Required for the very first state of the form to always be displayed.
#elif LINUX

			//Not for a window that was just iconified: an unmapped state is exactly what was asked for there,
			//so waiting for it to become viewable would only ever burn the whole grace period.
			if (!hide && !min && form.WindowState != FormWindowState.Minimized && !IsWaylandSession)
				WaitForX11Map(form.Handle);

#endif
			// Now that the window has a real size, resolve any deferred "+MinSize"/"+MaxSize" (no dimensions).
			if (!hide)
				ApplyDeferredSizeLimits();

			return DefaultObject;
		}

#if LINUX
		/// <summary>
		/// Blocks until the just-shown window is actually mapped on the X server, or a short grace period
		/// elapses. GTK maps a toplevel from its main loop, so <c>Show()</c> returns while the window is still
		/// unmapped - and an unmapped window is not viewable, so it is missing from the window enumeration the
		/// window functions search. Every WinWaitClose/WinWait/WinMove issued right after Show would then act
		/// on a window that appears not to exist (AHK's ShowWindow is synchronous, so scripts assume it does).
		/// </summary>
		/// <param name="handle">The form's X window id.</param>
		private static void WaitForX11Map(nint handle)
		{
			if (handle == 0)
				return;

			var win = new Keysharp.Internals.Window.Linux.Proxies.XWindow(Keysharp.Internals.Window.Linux.Proxies.XDisplay.Default, handle.ToInt64());
			var deadline = Environment.TickCount64 + 2000;
			//Stop on a failed read rather than treating it as "not mapped yet": the handle may be no X window
			//at all (a Wayland backend hands out widget pointers, and the session guard reads the session type
			//rather than the backend actually in use), or the window may already be gone. Attributes would
			//report the zeroed struct's IsUnmapped for both and burn the whole grace period.
			Keysharp.Internals.Flow.WaitWithMessagePump(() =>
				Environment.TickCount64 < deadline
				&& win.TryGetAttributes(out var attrs)
				&& attrs.map_state != Keysharp.Internals.Window.Linux.X11.MapState.IsViewable);
		}

#endif
        public KeysharpObject Submit(object hide = null)
		{
			var hideVal = ForceBool(hide ?? true);
			var panels = new HashSet<Panel>();
			var ctrls = form.GetControls().Flatten(true);
			var result = new KeysharpObject();

			foreach (Forms.Control control in ctrls)
			{
				if (control.Name != "" && control.GetGuiControl() is Gui.Control guictrl)
				{
					if (control is KeysharpTextBox || control is KeysharpPasswordBox || control is KeysharpDateTimePicker || control is KeysharpMonthCalendar)//Just use value because it's the same and consolidates the formatting in one place, despite being slightly slower.
						result.DefinePropInternal(control.Name, new OwnPropsDesc(null, guictrl.Value));
					else if (control is KeysharpRichEdit)
						result.DefinePropInternal(control.Name,  new OwnPropsDesc(null, !guictrl.AltSubmit ? guictrl.Value : guictrl.RichText));
					else if (control is KeysharpNumericUpDown nud)
					{
#if WINDOWS
						decimal v = decimal.Round(nud.Value, nud.DecimalPlaces);
						if (v == decimal.Truncate(v) && v >= long.MinValue && v <= long.MaxValue)
							result.DefinePropInternal(nud.Name, new OwnPropsDesc(null, (long)v));
						else
							result.DefinePropInternal(nud.Name, new OwnPropsDesc(null, (double)v));
#else
						double v = nud.Value;
						if (v == double.Truncate(v) && v >= long.MinValue && v <= long.MaxValue)
							result.DefinePropInternal(nud.Name, new OwnPropsDesc(null, (long)v));
						else
							result.DefinePropInternal(nud.Name, new OwnPropsDesc(null, v));
#endif
					}
					else if (control is KeysharpCheckBox cb)
#if WINDOWS
						result.DefinePropInternal(cb.Name, new OwnPropsDesc(null, cb.Checked ? 1L : 0L));
#else
						result.DefinePropInternal(cb.Name, new OwnPropsDesc(null, (cb.Checked ?? false) ? 1L : 0L));
#endif
					else if (control is KeysharpTabControl tc)
						result.DefinePropInternal(tc.Name, new OwnPropsDesc(null, !guictrl.AltSubmit ? tc.SelectedTab != null ? tc.SelectedTab.Text : "" : (long)(tc.SelectedIndex + 1)));
					else if (control is KeysharpComboBox cmb)
						result.DefinePropInternal(cmb.Name, new OwnPropsDesc(null, !guictrl.AltSubmit || cmb.Items.IndexOf(cmb.Text) == -1 ? cmb.Text : (long)(cmb.SelectedIndex + 1)));
					else if (control is TrackBar tb)
						result.DefinePropInternal(tb.Name, new OwnPropsDesc(null, tb.Value));
					else if (control is KeysharpListBox lb)
					{
						result.DefinePropInternal(lb.Name, new OwnPropsDesc(null, !guictrl.AltSubmit
									   ? guictrl.Value
									   : lb.SelectionMode == SelectionMode.One
									   ? lb.SelectedItem as string ?? ""
									   : new Array(lb.SelectedItems.Cast<object>().Where(xx => xx is string).Select(x => x as string))));
					}
					else if (control is RadioButton rb)//This is supposed to do something special if it's part of a group, but unsure how to determine that.
					{
						if (rb.Parent is Panel pnl && !panels.Contains(pnl))
						{
							_ = panels.Add(pnl);
							var rbs = pnl.Controls.Cast<Forms.Control>().Where(pc => pc is RadioButton pcrb).Cast<RadioButton>().ToList();
							var named = rbs.Where(rr => rr.Name != "").ToList();

							if (named.Count == 1)
							{
								for (var i = 0; i < rbs.Count(); i++)
								{
									if (rbs[i].Checked)
									{
										result.DefinePropInternal(named[0].Name, new OwnPropsDesc(null, (long)(i + 1)));
										goto DoneAssigning;
									}
								}

								result.DefinePropInternal(named[0].Name, new OwnPropsDesc(null, 0L));
							}
						}
					}
				}
			}

			DoneAssigning:

			if (hideVal)
				_ = Hide();

			return result;
		}

		public object UseGroup(object groupBox = null)
		{
			if (groupBox is Gui.Control gctrl && gctrl.Ctrl is KeysharpGroupBox gb)
				LastContainer = gb;
			else
				LastContainer = form;

			return DefaultObject;
		}

		/// <summary>
		/// Grows a container so that any dimension it was not given an explicit size for encloses the controls
		/// placed inside it. The "was it explicit" test reuses the holder's <c>requestedSize</c> (int.MinValue
		/// marks a dimension the script left unspecified); a dimension that was specified is left untouched, and
		/// an empty container keeps the default size computed when it was added. Generic over container type so
		/// it applies to any enter/exit-able container (today GroupBox; the chrome term is the only per-type bit).
		/// </summary>
		private void FitContainerToContent(Forms.Control container)
		{
			bool autoWidth, autoHeight;

			if (controls.TryGetValue(container.Handle.ToInt64(), out var holderObj) && holderObj is Gui.Control holder)
			{
				autoWidth = holder.requestedSize.Width == int.MinValue;
				autoHeight = holder.requestedSize.Height == int.MinValue;
			}
			else//Internal container with no holder (e.g. a radio-group panel) always tracks its content.
			{
				autoWidth = autoHeight = true;
			}

			if (!autoWidth && !autoHeight)
				return;

			var layout = container.GetLayoutContainer();

			if (layout == null)
				return;

			int maxRight = 0, maxBottom = 0;
			var hasContent = false;

			foreach (Forms.Control c in layout.Controls)
			{
				if (c is KeysharpStatusStrip)
					continue;

				var loc = c.GetLocation();
				var sz = c.GetSize();
				maxRight = Math.Max(maxRight, loc.X + sz.Width);
				maxBottom = Math.Max(maxBottom, loc.Y + sz.Height);
				hasContent = true;
			}

			if (!hasContent)
				return;//Nothing inside yet - leave the default size in place.

			var cur = container.GetSize();
			int padW, padH;

			if (container is KeysharpGroupBox gb)
			{
#if WINDOWS
				//Children are laid out in the group's client area; pad by the non-client chrome (border) plus a
				//small inner margin so the bottom-most/right-most control isn't flush against the frame.
				padW = Math.Max(0, cur.Width - gb.ClientSize.Width) + gb.Margin.Right + 4;
				padH = Math.Max(0, cur.Height - gb.ClientSize.Height) + gb.Margin.Bottom + 6;
#else
				//On GTK the title label sits above the content area (roughly a text line of chrome); widths only
				//need the frame's side insets.
				padW = 14;
				padH = (int)Math.Ceiling(GetFontPixels(gb.Font)) + 16;
#endif
			}
			else//Borderless container: just clear its margin past the content.
			{
				padW = container.Margin.Right;
				padH = container.Margin.Bottom;
			}

			var newW = autoWidth ? maxRight + padW : cur.Width;
			var newH = autoHeight ? maxBottom + padH : cur.Height;

			if (newW != cur.Width || newH != cur.Height)
				container.SetSize(new Size(newW, newH));
		}

		IEnumerator IEnumerable.GetEnumerator() => CreateEnumerator(2);

		private Enumerator CreateEnumerator(int count)
		{
			var iter = controls.GetEnumerator();

			return new Enumerator(
					   this,
					   count,
					   () => iter.MoveNext(),
					   () => iter.Current.Value,
					   () => (iter.Current.Key, iter.Current.Value),
					   () => iter = controls.GetEnumerator());
		}

		internal static bool AnyExistingVisibleWindows() => Script.TheScript.GuiData.allGuiHwnds.Values.Any(g => g.form != null && g.form.Visible);

		internal static void DestroyAll()
		{
			var script = Script.TheScript;

			//Destroy everything but the main window, which will destroy itself.
			foreach (var gui in script.GuiData.allGuiHwnds.Values.Where(g => g.form != script.mainWindow).ToArray())
			{
				try
				{
					_ = gui.Destroy();
				}
				catch
				{
				}
			}

			script.GuiData.allGuiHwnds.Clear();
		}

		internal static float GetFontPixels(Font font) => font.GetHeight((float)A_ScreenDPI);

		internal static bool IsGuiType(Type type) => GuiTypes.Any(t => t.IsAssignableFrom(type));

		//AHK lets Checked/Hidden/Disabled be followed by an optional 1/0/-1 so a script can pass a stored flag
		//straight through ("Hidden" VarContainingOne). A suffix which evaluates to zero flips the sign, so
		//"Hidden0" means the same as "-Hidden". A non-numeric suffix reads as zero, like AHK's ATOI().
		private static bool ApplySuffixFlag(ReadOnlySpan<char> suffix, bool adding) =>
		suffix.Length > 0 && (!int.TryParse(suffix, out var n) || n == 0) ? !adding : adding;

		//Maps the side letter of a slider's "ToolTip<side>" option onto the TBTS_* value it selects. AHK reads
		//only the first letter, so "ToolTipL" and "ToolTipLeft" are the same option.
		private static int TooltipSide(char side) => char.ToUpperInvariant(side) switch
		{
			'L' => 1,
			'B' => 2,
			'R' => 3,
			_ => 0,//Top, which is also the default when no side is given.
		};

		internal GuiOptions ParseOpt(string type, string text, string optionsstr)
		{
			//Word() matches an option that is just a word, Val() one whose word is followed by a value, handing
			//back the part after the word. Both are case-insensitive, and having them keeps that noise out of the
			//ladder below. Options whose value is a number or a color go through Options.TryParse() instead,
			//which already understands hex, an optional value and a default. (These take the option as a
			//parameter rather than capturing it because a local function cannot close over a span.)
			static bool Word(ReadOnlySpan<char> opt, string word) => opt.Equals(word, StringComparison.OrdinalIgnoreCase);

			static bool Val(ReadOnlySpan<char> opt, string word, out ReadOnlySpan<char> value)
			{
				var matched = opt.StartsWith(word, StringComparison.OrdinalIgnoreCase);
				value = matched ? opt[word.Length..] : default;
				return matched;
			}

			var options = new GuiOptions();
			//Add() lowercases the type, but GuiControl.Opt() passes the name the script used ("DateTime"),
			//so normalize once here rather than making every type test below case-insensitive.
			type = type?.ToLowerInvariant() ?? "";

			if (type == Keyword_MonthCal && !string.IsNullOrEmpty(text))
			{
				Conversions.ParseRange(text, out options.dtselstart, out options.dtselend);

				if (options.dtselstart == System.DateTime.MinValue)
					options.dtselstart = options.dtselend;

				if (options.dtselend == System.DateTime.MaxValue)
					options.dtselend = options.dtselstart;

				if (options.dtselstart != System.DateTime.MinValue && options.dtselend != System.DateTime.MaxValue &&
						(options.dtselend - options.dtselstart).TotalDays > 1)
					options.datemultisel = true;
			}

			foreach (Range r in optionsstr.AsSpan().SplitAny(Spaces))
			{
				var raw = optionsstr.AsSpan(r).Trim();

				if (raw.Length == 0)
					continue;

				//AHK strips the sign once, up front: "-Word" removes the property, "+Word" adds it, and a bare
				//"Word" is an implicit "+Word" ("In the absence of a preceding sign, a plus sign is assumed").
				//Every test below therefore matches the bare option word and consults adding for the sign,
				//instead of each one having to cope with a sign of its own.
				var adding = raw[0] != '-';
				var opt = raw[0] is '+' or '-' ? raw[1..] : raw;

				if (opt.Length == 0)
					continue;//A lone "+" or "-", which AHK ignores.

				var temp = 0;
				var tempcolor = Color.Empty;

				if (type == Keyword_DateTime)
				{
					//ChooseNone has to be tested first because the date parser accepts any suffix.
					if (Word(opt, "ChooseNone")) { options.choosenone = adding; continue; }
					else if (Val(opt, "Choose", out _))
					{
						if (adding)//AHK does not implement removal of a chosen date.
							_ = Options.TryParseDateTime(opt, "Choose", "yyyyMMdd", ref options.dtChoose);

						continue;
					}
					else if (Word(opt, "1")) { options.dtopt1 = adding; continue; }//DTS_UPDOWN.
					else if (Word(opt, "2")) { options.dtopt2 = adding; continue; }//DTS_SHOWNONE.
				}
				else if (type == Keyword_MonthCal)
				{
					//MCS_WEEKNUMBERS/MCS_NOTODAYCIRCLE/MCS_NOTODAY, which AHK spells as plain style numbers.
					if (Word(opt, "4")) { options.opt4 = adding; continue; }
					else if (Word(opt, "8")) { options.opt8 = adding; continue; }
					else if (Word(opt, "16")) { options.opt16 = adding; continue; }
				}

				//"Multi" is multi-line for Edit and multi-select for ListBox/ListView/MonthCal.
				if (Word(opt, "Multi"))
				{
					if (type == Keyword_MonthCal)
						options.datemultisel = adding;
					else
						options.multiline = adding;

					continue;
				}

				//Options carrying a number are matched first: each demands a numeric value, so none of them can
				//swallow a word option that merely starts with the same letter ("Range", "Redraw", "Hidden"...).
				if (Options.TryParse(opt, "r", ref options.rows)) { }
				else if (Options.TryParse(opt, "w", ref options.width)) { }
				else if (Options.TryParse(opt, "h", ref options.height)) { }
				else if (Options.TryParse(opt, "x+", ref options.x)) { options.xpos = GuiOptions.Positioning.PreviousBottomRight; }
				else if (Options.TryParse(opt, "y+", ref options.y)) { options.ypos = GuiOptions.Positioning.PreviousBottomRight; }
				else if (Options.TryParse(opt, "x", ref options.x)) { options.xpos = GuiOptions.Positioning.Absolute; }
				else if (Options.TryParse(opt, "y", ref options.y)) { options.ypos = GuiOptions.Positioning.Absolute; }
				else if (Options.TryParse(opt, "t", ref options.t)) { options.tabstops.Add(options.t); }
				else if (Word(opt, "Redraw")) { options.redraw = adding; }
				else if (Word(opt, "DPIResize")) { options.dpiresize = adding; }
				//Checkbox.
				else if (Word(opt, "Check3")) { options.check3 = adding; }//Needs to come before any option starting with a 'c'.
				else if (Word(opt, "CheckedGray")) { options.checkedgray = adding; options.ischecked = adding ? -1 : 0; }
				//AHK allows an explicit 1/0/-1 after the word so a script can pass a stored flag straight through;
				//without one the sign decides.
				else if (Options.TryParse(opt, "Checked", ref temp, StringComparison.OrdinalIgnoreCase, true, adding ? 1 : 0)) { options.ischecked = temp; }
				else if (Val(opt, "Range", out var range))
				{
					if (adding)//AHK does not implement range removal.
					{
						options.nudrange = range.ToString();

						if (type == Keyword_DateTime || type == Keyword_MonthCal)
						{
							Conversions.ParseRange(options.nudrange, out options.dtlow, out options.dthigh);
						}
						else if (type == Keyword_UpDown || type == Keyword_Slider || type == Keyword_Progress)
						{
							var vals = Conversions.ParseRange(options.nudrange.Split('-', StringSplitOptions.None));

							if (vals.Count > 0)
								options.nudlow = vals[0];

							if (vals.Count > 1)
								options.nudhigh = vals[1];
						}
					}
				}
				else if (Options.TryParse(opt, "Choose", ref temp)) { if (adding) { options.ddlchoose = temp - 1; options.choose.Add(options.ddlchoose); } }
				//
				else if (Word(opt, "Vertical")) { options.vertical = adding; }
				else if (Val(opt, "Disabled", out var disabled)) { options.enabled = !ApplySuffixFlag(disabled, adding); }
				else if (Val(opt, "Hidden", out var hidden)) { options.visible = !ApplySuffixFlag(hidden, adding); }
				else if (Word(opt, "Autosize")) { options.autosize = adding; }
				else if (Options.TryParse(opt, "wp", ref options.wp, StringComparison.OrdinalIgnoreCase, true)) { }
				else if (Options.TryParse(opt, "hp", ref options.hp, StringComparison.OrdinalIgnoreCase, true)) { }

#if WINDOWS
				else if (Options.TryParseString(opt, "Class", ref options.customclass, StringComparison.OrdinalIgnoreCase)) { }

#endif
				else if (Options.TryParse(opt, "xp", ref options.x, StringComparison.OrdinalIgnoreCase, true)) { options.xpos = GuiOptions.Positioning.PreviousTopLeft; }
				else if (Options.TryParse(opt, "yp", ref options.y, StringComparison.OrdinalIgnoreCase, true)) { options.ypos = GuiOptions.Positioning.PreviousTopLeft; }
				else if (Options.TryParse(opt, "xm", ref options.x, StringComparison.OrdinalIgnoreCase, true)) { options.xpos = GuiOptions.Positioning.Margin; }
				else if (Options.TryParse(opt, "ym", ref options.y, StringComparison.OrdinalIgnoreCase, true)) { options.ypos = GuiOptions.Positioning.Margin; }
				else if (Options.TryParse(opt, "x+m", ref options.x, StringComparison.OrdinalIgnoreCase, true)) { options.x = form.Margin.Left; options.xpos = GuiOptions.Positioning.PreviousBottomRight; }
				else if (Options.TryParse(opt, "y+m", ref options.y, StringComparison.OrdinalIgnoreCase, true)) { options.y = form.Margin.Bottom; options.ypos = GuiOptions.Positioning.PreviousBottomRight; }
				else if (Options.TryParse(opt, "xs", ref options.x, StringComparison.OrdinalIgnoreCase, true)) { options.xpos = GuiOptions.Positioning.Section; }
				else if (Options.TryParse(opt, "ys", ref options.y, StringComparison.OrdinalIgnoreCase, true)) { options.ypos = GuiOptions.Positioning.Section; }
				else if (Options.TryParse(opt, "xc", ref options.x, StringComparison.OrdinalIgnoreCase, true)) { options.xpos = GuiOptions.Positioning.Container; }
				else if (Options.TryParse(opt, "yc", ref options.y, StringComparison.OrdinalIgnoreCase, true)) { options.ypos = GuiOptions.Positioning.Container; }
				else if (Word(opt, "AltSubmit")) { options.altsubmit = adding; }
				else if (Word(opt, "Section")) { options.section = true; }//AHK treats adding and removing the same here.
				else if (Word(opt, "Tabstop")) { options.tabstop = adding; }
				else if (Word(opt, "Wrap")) { options.wordwrap = adding; }
				else if (Word(opt, "VScroll")) { options.vscroll = adding; }
				//The optional number after HScroll is the horizontal scrolling width (ListBox only).
				else if (Options.TryParse(opt, "HScroll", ref temp, StringComparison.OrdinalIgnoreCase, true)) { options.hscroll = adding; if (adding) options.hscrollamt = temp; }
				else if (Options.TryParse(opt, "Increment", ref temp)) { options.nudinc = temp; }
				else if (Word(opt, "Hex")) { options.hex = adding; }
				else if (Word(opt, "BackgroundTrans"))
				{
					options.bgtrans = adding;

					//AHK's "-BackgroundTrans" resets the background the same way "+Background" does.
					if (adding)
						options.addexstyle |= 0x00000020;
					else
						options.bgcolor = Forms.Control.DefaultBackColor;
				}
				//"+Background" (no suffix), "-Background" and "BackgroundDefault" all revert to the default color,
				//and so does any other negated spelling: only "+Background<color>" picks a color.
				else if (Word(opt, "Background") || Word(opt, "BackgroundDefault")) { options.bgcolor = Forms.Control.DefaultBackColor; }
				else if (Options.TryParse(opt, "Background", ref tempcolor, StringComparison.OrdinalIgnoreCase, true)) { options.bgcolor = adding ? tempcolor : Forms.Control.DefaultBackColor; }
				else if (Word(opt, "Border")) { options.thinborder = adding; }

				//Removing an alignment reverts to the default (left/top), since there is no way to know what the
				//previous value was; this is what AHK does by clearing the corresponding style bits.
				else if (Word(opt, "Left")) { if (adding) options.halign = GuiOptions.HorizontalAlignment.Left; }
				else if (Word(opt, "Center")) { options.halign = adding ? GuiOptions.HorizontalAlignment.Center : GuiOptions.HorizontalAlignment.Left; }
				else if (Word(opt, "Right")) { options.halign = adding ? GuiOptions.HorizontalAlignment.Right : GuiOptions.HorizontalAlignment.Left; }
				else if (Word(opt, "Bottom")) { options.valign = adding ? GuiOptions.VerticalAlignment.Bottom : GuiOptions.VerticalAlignment.Top; }
				else if (Word(opt, "Top")) { if (adding) options.valign = GuiOptions.VerticalAlignment.Top; }
				else if (Word(opt, "Middle")) { options.valign = adding ? GuiOptions.VerticalAlignment.Middle : GuiOptions.VerticalAlignment.Top; }

				//Control specific.
				//Edit. "-Limit" removes the limit, which WinForms and Eto both spell as a maximum length of zero.
				else if (Options.TryParse(opt, "Limit", ref temp, StringComparison.OrdinalIgnoreCase, true)) { options.limit = adding ? temp : 0; }
				else if (Word(opt, "Lowercase")) { options.lowercase = adding; }
				else if (Word(opt, "Uppercase")) { options.uppercase = adding; }
				else if (Word(opt, "Number")) { options.number = adding; }
				else if (Val(opt, "Password", out var pwdch)) { options.pwd = adding; options.pwdch = adding ? pwdch.ToString() : ""; }
				else if (Word(opt, "ReadOnly")) { options.rdonly = adding; }
				else if (Word(opt, "WantCtrlA")) { options.wantctrla = adding; }
				else if (Word(opt, "WantReturn")) { options.wantreturn = adding; }
				else if (Word(opt, "WantTab")) { options.wanttab = adding; }
				//GrouBox.
				else if (Word(opt, "Group")) { options.group = adding; }
				//UpDown.
				else if (Word(opt, "Horz")) { options.nudhorz = adding; }
				//16
				//0x80
				//None unit inc/dec
				//Button.
				else if (Word(opt, "Default")) { options.btndef = adding; }
				//DropDownList.
				else if (Word(opt, "Sort")) { options.sort = adding; }
				//ComboBox.
				else if (Word(opt, "Simple")) { options.cmbsimple = adding; }
				else if (Word(opt, "Invert")) { options.invert = adding; }
				else if (Options.TryParse(opt, "Line", ref options.line)) { }
				else if (Word(opt, "NoTicks")) { options.noticks = adding; }
				else if (Options.TryParse(opt, "Page", ref options.page)) { }
				else if (Options.TryParse(opt, "Thick", ref options.thick)) { }
				else if (Options.TryParse(opt, "TickInterval", ref options.tickinterval)) { }
				//AHK reads only the first letter of the side, so "ToolTipL" and "ToolTipLeft" are the same option.
				else if (Val(opt, "ToolTip", out var side)) { options.tooltip = adding; if (adding) options.tooltipside = side.Length > 0 ? TooltipSide(side[0]) : 0; }
				else if (Word(opt, "Smooth")) { options.smooth = adding; }
				else if (Word(opt, "Buttons")) { options.buttons = adding; }
				else if (Options.TryParse(opt, "ImageList", ref options.ilid)) { }
				else if (Word(opt, "Lines")) { options.lines = adding; }
				else if (Word(opt, "WantF2")) { options.wantf2 = adding; }
				//ListView.
				else if (Word(opt, "SortDesc")) { options.sortdesc = adding; }
				else if (Word(opt, "Grid")) { options.grid = adding; }
				else if (Word(opt, "Hdr")) { options.header = adding; }
				else if (Word(opt, "NoSortHdr")) { options.clickheader = !adding; }
				else if (Word(opt, "NoSort")) { options.sortheader = !adding; }
				//"Icon" picks the view for a ListView ("Icon"/"IconSmall") but the icon index for a Picture.
				else if (Val(opt, "Icon", out var icon))
				{
					//AHK does not implement removal here.
					if (adding && type == Keyword_ListView)
						options.lvview = icon.Equals("Small", StringComparison.OrdinalIgnoreCase) ? View.SmallIcon : View.LargeIcon;
					else if (adding && icon.Length > 0)
						options.iconnumber = ImageHelper.PrepareIconNumber(icon.ToString());
				}
				else if (Word(opt, "Tile")) { if (adding) options.lvview = View.Tile; }
				else if (Word(opt, "List")) { if (adding) options.lvview = View.List; }
				else if (Word(opt, "Report")) { if (adding) options.lvview = View.Details; }
				//Other. "-c" reverts to the system text color, which is what Options.TryParse() hands back for
				//"cDefault" as well.
				else if (Options.TryParse(opt, "c", ref tempcolor, StringComparison.OrdinalIgnoreCase, !adding)) { options.c = tempcolor; }
				//Font style options. These mirror the subset of Gui.SetFont() options that don't need a font
				//family name (which can't be parsed unambiguously from a space-delimited option string, e.g.
				//"MS Sans Serif"). The tokens are accumulated and applied to the control's font in Add() via
				//Conversions.ParseFont(). "s" is only matched when followed by a number, so it doesn't clash
				//with word options such as Section/Sort/Smooth. ParseFont() has no syntax for removing a single
				//style (use "norm" to clear them all), so a negated token is accepted but has no effect.
				else if (Options.TryParse(opt, "s", ref temp)
						 || Word(opt, Keyword_Bold)
						 || Word(opt, Keyword_Italic)
						 || Word(opt, Keyword_Strike)
						 || Word(opt, Keyword_Underline)
						 || Word(opt, Keyword_Norm)) { if (adding) options.fontstyles += " " + opt.ToString(); }
				//The name must be matched after every word option, otherwise it swallows them ("VScroll" would
				//be read as the name "Scroll"), which is also why AHK checks its single-letter options last.
				else if (Options.TryParseString(opt, "v", ref options.name)) { }
				//Raw style numbers: a leading E is an extended style, LV a ListView extended style, and a bare
				//number a window style. The sign decides whether each is added or removed.
				else if (Options.TryParse(opt, "E", ref temp)) { if (adding) options.addexstyle |= temp; else options.remexstyle |= temp; }
				else if (Options.TryParse(opt, "LV", ref temp)) { if (adding) options.addlvstyle |= temp; else options.remlvstyle |= temp; }
				else if (Options.TryParse(opt, "", ref temp)) { if (adding) options.addstyle |= temp; else options.remstyle |= temp; }
				//Anything left is a raw token that isn't a recognized option and isn't a numeric style: reject
				//it like Gui window options do, instead of silently ignoring it.
				else { _ = Errors.ValueErrorOccurred("Invalid option.", raw.ToString()); }
			}

			return options;
		}

		internal static void SuppressCtrlAKeyDown(object o, KeyEventArgs e)
		{
#if WINDOWS
			if (e.KeyData == (Keys.Control | Keys.A))
				e.SuppressKeyPress = true;
#else
			// CommonModifier is Cmd on macOS and Ctrl elsewhere, so -WantCtrlA suppresses the
			// platform's native select-all shortcut.
			if (e.KeyData == (Forms.Application.Instance.CommonModifier | Forms.Keys.A))
				e.Handled = true;
#endif
		}

		internal static void SetContentAlignment(Forms.Control ctrl, GuiOptions opts)
		{
#if WINDOWS
			if (!opts.halign.HasValue && !opts.valign.HasValue)
				return;

			//Alignment reaches the control through its TextAlign property, which text-bearing controls spell as a
			//two-dimensional ContentAlignment (Label/Button/CheckBox/Radio) but Edit-like controls spell as a
			//horizontal-only HorizontalAlignment. Control types that have neither must be left alone: a Slider
			//takes Left/Center from opts.halign to place its tick marks instead (see Add()), and a ProgressBar has
			//no notion of alignment at all, so reading a TextAlign they don't have would throw.
			var ctrlType = ctrl.GetType();

			if (ctrlType.GetProperty("TextAlign", typeof(ContentAlignment)) is PropertyInfo content && content.CanRead && content.CanWrite)
			{
				var current = (ContentAlignment)content.GetValue(ctrl);
				content.SetValue(ctrl, CombineAlignment(opts.valign ?? GetVerticalAlignment(current), opts.halign ?? GetHorizontalAlignment(current)));
			}
			else if (opts.halign.HasValue && ctrlType.GetProperty("TextAlign", typeof(Forms.HorizontalAlignment)) is PropertyInfo horizontal && horizontal.CanWrite)
			{
				horizontal.SetValue(ctrl, ToFormsAlignment(opts.halign.Value));
			}
#else
			if (opts.halign.HasValue)
			{
				if (opts.halign.Value == GuiOptions.HorizontalAlignment.Center)
					Reflections.SafeSetProperty(ctrl, "TextAlignment", TextAlignment.Center);
				else if (opts.halign.Value == GuiOptions.HorizontalAlignment.Left)
					Reflections.SafeSetProperty(ctrl, "TextAlignment", TextAlignment.Left);
				else if (opts.halign.Value == GuiOptions.HorizontalAlignment.Right)
					Reflections.SafeSetProperty(ctrl, "TextAlignment", TextAlignment.Right);
			}
			if (opts.valign.HasValue)
			{
				if (opts.valign.Value == GuiOptions.VerticalAlignment.Middle)
					Reflections.SafeSetProperty(ctrl, "VerticalAlignment", VerticalAlignment.Center);
				else if (opts.valign.Value == GuiOptions.VerticalAlignment.Top)
					Reflections.SafeSetProperty(ctrl, "VerticalAlignment", VerticalAlignment.Top);
				else if (opts.valign.Value == GuiOptions.VerticalAlignment.Bottom)
					Reflections.SafeSetProperty(ctrl, "VerticalAlignment", VerticalAlignment.Bottom);
			}
#endif
		}


#if WINDOWS
		internal static void SuppressCtrlAPreviewKeyDown(object o, PreviewKeyDownEventArgs e)
		{
			if (e.KeyData == (Keys.Control | Keys.A))
				e.IsInputKey = true;
		}

		//Edit-like controls only have a horizontal TextAlign, which is a different enum than the Label/Button one.
		private static Forms.HorizontalAlignment ToFormsAlignment(GuiOptions.HorizontalAlignment alignment) => alignment switch
		{
			GuiOptions.HorizontalAlignment.Center => Forms.HorizontalAlignment.Center,
			GuiOptions.HorizontalAlignment.Right => Forms.HorizontalAlignment.Right,
			_ => Forms.HorizontalAlignment.Left,
		};

		private static GuiOptions.VerticalAlignment GetVerticalAlignment(ContentAlignment alignment) => alignment switch
		{
			ContentAlignment.TopLeft or ContentAlignment.TopCenter or ContentAlignment.TopRight => GuiOptions.VerticalAlignment.Top,
			ContentAlignment.MiddleLeft or ContentAlignment.MiddleCenter or ContentAlignment.MiddleRight => GuiOptions.VerticalAlignment.Middle,
			_ => GuiOptions.VerticalAlignment.Bottom
		};

		private static GuiOptions.HorizontalAlignment GetHorizontalAlignment(ContentAlignment alignment) => alignment switch
		{
			ContentAlignment.TopLeft or ContentAlignment.MiddleLeft or ContentAlignment.BottomLeft => GuiOptions.HorizontalAlignment.Left,
			ContentAlignment.TopCenter or ContentAlignment.MiddleCenter or ContentAlignment.BottomCenter => GuiOptions.HorizontalAlignment.Center,
			_ => GuiOptions.HorizontalAlignment.Right
		};

		private static ContentAlignment CombineAlignment(GuiOptions.VerticalAlignment vertical, GuiOptions.HorizontalAlignment horizontal) => (vertical, horizontal) switch
		{
			(GuiOptions.VerticalAlignment.Top, GuiOptions.HorizontalAlignment.Left) => ContentAlignment.TopLeft,
			(GuiOptions.VerticalAlignment.Top, GuiOptions.HorizontalAlignment.Center) => ContentAlignment.TopCenter,
			(GuiOptions.VerticalAlignment.Top, GuiOptions.HorizontalAlignment.Right) => ContentAlignment.TopRight,
			(GuiOptions.VerticalAlignment.Middle, GuiOptions.HorizontalAlignment.Left) => ContentAlignment.MiddleLeft,
			(GuiOptions.VerticalAlignment.Middle, GuiOptions.HorizontalAlignment.Center) => ContentAlignment.MiddleCenter,
			(GuiOptions.VerticalAlignment.Middle, GuiOptions.HorizontalAlignment.Right) => ContentAlignment.MiddleRight,
			(GuiOptions.VerticalAlignment.Bottom, GuiOptions.HorizontalAlignment.Left) => ContentAlignment.BottomLeft,
			(GuiOptions.VerticalAlignment.Bottom, GuiOptions.HorizontalAlignment.Center) => ContentAlignment.BottomCenter,
			_ => ContentAlignment.BottomRight
		};
#endif

		internal static void Tv_Lv_KeyDown(object sender, KeyEventArgs e)
		{
#if WINDOWS
			if (e.KeyCode == Keys.F2)
			{
				if (sender is KeysharpTreeView tv)
					tv.SelectedNode?.BeginEdit();
				else if (sender is KeysharpListView lv && lv.SelectedItems.Count > 0)
					lv.SelectedItems[0].BeginEdit();
			}
#else
			if (e.Key == Forms.Keys.F2)
			{
				if (sender is KeysharpTreeView tv)
					tv.BeginLabelEdit(tv.SelectedNode);
				else if (sender is KeysharpListView lv && lv.SelectedItems.Count > 0)
					lv.SelectedItems[0].BeginEdit();
			}
#endif
		}

		private static void Opt(object obj, ref int addStyle, ref int addExStyle, ref int removeStyle, ref int removeExStyle)
		{
#if WINDOWS
			var options = obj.As();
			var tempbool = false;

			// These are raw Win32 WS_/WS_EX_ style bits (e.g. +E0x8) with no portable equivalent, so this method is
			// a no-op on non-Windows; portable window attributes are expressed through Eto's typed properties
			// rather than raw style numbers.
			foreach (var raw in Options.ParseOptions(options))
			{
				// An option with no leading +/- means + (add), per AHK. Normalize so the sign-aware checks below see an
				// explicit sign; otherwise a no-sign flag like "AlwaysOnTop" parses as false (empty suffix => default) and
				// is never applied — leaving the window non-topmost.
				var split = (raw.Length > 0 && raw[0] != '+' && raw[0] != '-') ? "+" + raw : raw;
				var str = split.Substring(1);

				if (str.Length > 0)
				{
					var temp = 0;

					if (Options.TryParse(split, "+E", ref temp) || Options.TryParse(split, "E", ref temp))
					{
						addExStyle |= temp;
					}
					else if (Options.TryParse(split, "-E", ref temp))
					{
						removeExStyle |= temp;
					}
					else if (Options.TryParse(split, "-", ref temp))
					{
						removeStyle |= temp;
					}
					else if (Options.TryParse(split, "+", ref temp))
					{
						addStyle |= temp;
					}
					else if (Options.TryParse(split, "AlwaysOnTop", ref tempbool, StringComparison.OrdinalIgnoreCase, true, false))
					{
						if (tempbool)
							addExStyle |= 0x00000008;
						else
							addExStyle &= ~0x00000008;
					}
					else if (Options.TryParse(split, "", ref temp))
					{
						addStyle |= temp;
					}
				}
			}

#endif
		}

		private void ResizeTabControls()
		{
			if (controls.Count == 0)
				return;

			var dpiscale = DpiScale;

			foreach (var val in controls.Values)
				if (val is Gui.Control gc && gc.Ctrl is KeysharpTabControl ktc)
					ktc.AdjustSize(dpiscale, gc.requestedSize);
		}

		public object this[object controlname]
		{
			get
			{
				var handle = controlname.ParseLong();

				if (handle.HasValue)
				{
					if (controls.TryGetValue(handle.Value, out var val) && val is Gui.Control gc)
						return gc;
				}

				if (controlname is string s)
				{
					foreach (var ctrlkv in controls)
					{
						if (ctrlkv.Value is Gui.Control gc)
						{
							if (string.Compare(gc.Name as string, s, true) == 0)
								return gc;

							if (string.Compare(gc.Text as string, s, true) == 0)
								return gc;
						}
					}

					//Put the ClassNN searches in a separate loop to be done as a last resort because they're very slow.
					foreach (var ctrlkv in controls)
					{
						if (ctrlkv.Value is Gui.Control gc)
						{
							if (string.Compare(gc.ClassNN, s, true) == 0)
								return gc;
						}
					}

					foreach (var ctrlkv in controls)
					{
						if (ctrlkv.Value is Gui.Control gc)
						{
							if (string.Compare(gc.NetClassNN, s, true) == 0)
								return gc;
						}
					}
				}

				return Script.CompatReturnsUnsetForMissing ? null
					: Errors.ErrorOccurred($"No controls matched the handle, name, text, ClassNN or NetClassNN {controlname}.");
			}
		}

		//Create a thin wrapper for each control type so that type checking like:
		//if (obj is Gui.Edit)
		//works correctly.
		public class ActiveX(params object[] args) : Gui.Control(args) { }

		public class Button(params object[] args) : Gui.Control(args) { }

		public class CheckBox(params object[] args) : Gui.Control(args) { }
#if WINDOWS
		public class Custom(params object[] args) : Gui.Control(args) { }
#endif
		public class DateTime(params object[] args) : Gui.Control(args) { }

		public class Edit(params object[] args) : Gui.Control(args) { }

		public class GroupBox(params object[] args) : Gui.Control(args) { }

		public class Hotkey(params object[] args) : Gui.Control(args) { }

		public class Link(params object[] args) : Gui.Control(args) { }

		public class List(params object[] args) : Gui.Control(args) { }

		public class ComboBox(params object[] args) : Gui.List(args) { }

		public class DDL(params object[] args) : Gui.List(args) { }

		public class ListBox(params object[] args) : Gui.List(args) { }

		public class Tab(params object[] args) : Gui.List(args) { }

		public class ListView(params object[] args) : Gui.Control(args) { }

		public class MonthCal(params object[] args) : Gui.Control(args) { }

		public class Pic(params object[] args) : Gui.Control(args) { }

		public class Progress(params object[] args) : Gui.Control(args) { }

		public class Radio(params object[] args) : Gui.Control(args) { }

		public class RichEdit(params object[] args) : Gui.Control(args) { }

		public class Slider(params object[] args) : Gui.Control(args) { }

		public class StatusBar(params object[] args) : Gui.Control(args) { }

		public class Text(params object[] args) : Gui.Control(args) { }

		public class TreeView(params object[] args) : Gui.Control(args) { }

		public class UpDown(params object[] args) : Gui.Control(args) { }

		public class WebBrowser(params object[] args) : Gui.Control(args) { }

		internal class GuiOptions
		{
			internal int addexstyle = 0;
			internal int addlvstyle = 0x20;
			internal int addstyle = 0;
			internal bool? altsubmit;
			internal bool? autosize;
			internal Color? bgcolor;
			internal bool bgtrans = false;

			//Ctrl specific.
			//Button.
			internal bool? btndef;

			//Tab.
			internal bool? buttons;

			internal Color? c;

			//Font style options (sN/bold/italic/strike/underline/norm) accumulated from the options string,
			//applied to the control's font in Add(). A font family name is intentionally not supported here.
			internal string fontstyles = "";

			//Checkbox.
			internal bool check3 = false;

			internal bool checkedgray = false;
			internal List<int> choose = [];
			internal bool choosenone = false;
			internal bool? clickheader;

			//ComboBox.
			internal bool? cmbsimple;
#if WINDOWS
			//Custom.
			internal string customclass = "";
#endif
			//DateTime.
			internal string customdate = "";

			internal bool datemultisel;

			//DropDownList
			internal int ddlchoose = int.MinValue;

			//Whether this control is re-laid out when the GUI's DPI changes; unset means inherit the GUI default.
			internal bool? dpiresize;

			internal System.DateTime dtChoose = System.DateTime.Now;
			internal System.DateTime dthigh = System.DateTime.MaxValue;
			internal System.DateTime dtlow = System.DateTime.MinValue;
			internal bool dtopt1 = false;
			internal bool dtopt2 = false;
			internal System.DateTime dtselend = System.DateTime.MaxValue;
			internal System.DateTime dtselstart = System.DateTime.MinValue;
			internal bool? enabled;

			//ListView.
			internal bool? grid;

			//GroupBox.
			internal bool group = false;

			internal bool? header;
			internal int height = int.MinValue;
			internal bool? hex;
			internal int hp = int.MinValue;
			internal bool hscroll = true;
			internal int hscrollamt = int.MinValue;

			//PictureBox.
			internal object iconnumber = 0;

			//TreeView.
			internal long ilid = long.MinValue;

			//Slider.
			internal bool? invert;

			internal int? ischecked;
			internal HorizontalAlignment? halign;
			internal VerticalAlignment? valign;

			//Edit.
			internal int limit = int.MinValue;

			internal int line = int.MinValue;
			internal bool? lines;
			internal bool? lowercase;
			internal View? lvview;
			internal bool? multiline;
			internal string name = null;
			internal bool? noticks;
			internal int? nudhigh;

			//NumericUpDown.
			internal bool nudhorz = false;

			internal int? nudinc;
			internal int? nudlow;
			internal string nudrange = "";
			internal bool number = false;
			internal bool opt16 = false;

			//MonthCal.
			internal bool opt4 = false;

			internal bool opt8 = false;
			internal int page = int.MinValue;
			internal bool pwd = false;
			internal string pwdch = "";
			internal bool? rdonly;
			internal bool? redraw;
			internal int remexstyle = 0;
			internal int remlvstyle;
			internal int remstyle = 0;
			internal float rows = float.MinValue;
			internal bool section = false;

			//Progress.
			internal bool? smooth;

			internal bool? sort;
			internal bool? sortdesc;
			internal bool? sortheader;
			internal int t = int.MinValue;
			internal bool? tabstop;
			internal List<int> tabstops = [];
			internal int thick = int.MinValue;
			internal bool? thinborder;
			internal int tickinterval = int.MinValue;
			internal bool tooltip = false;
			internal int tooltipside = 0;
			internal bool? uppercase;
			internal bool vertical = false;
			internal bool? visible;
			internal bool? vscroll;
			internal bool? wantctrla;
			internal bool? wantf2;
			internal bool? wantreturn;
			internal bool? wanttab;
			internal int width = int.MinValue;
			internal bool? wordwrap;
			internal int wp = int.MinValue;
			internal int x = int.MinValue;
			internal int y = int.MinValue;
			internal Positioning xpos = Positioning.None;
			internal Positioning ypos = Positioning.None;

			internal enum Positioning
			{
				None,
				Absolute,
				PreviousBottomRight,
				PreviousTopLeft,
				Container,
				Margin,
				Section,
			}

			internal enum HorizontalAlignment
			{
				Left,
				Center,
				Right
			}

			internal enum VerticalAlignment
			{
				Top,
				Middle,
				Bottom
			}
		}
	}

#if WINDOWS
	/// <summary>Bare IWin32Window over a foreign handle for Form.Show(owner) — no subclassing.</summary>
	internal sealed class OwnerHandle(nint handle) : IWin32Window
	{
		public nint Handle { get; } = handle;
	}
#endif

}
