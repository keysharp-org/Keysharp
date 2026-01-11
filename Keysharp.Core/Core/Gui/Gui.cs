namespace Keysharp.Core
{
	internal class GuiData
	{
		internal int windowCount = 0;
		internal ConcurrentDictionary<long, Gui> allGuiHwnds = new ();
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
#if WINDOWS
		internal bool dpiscaling = true;
#else
		internal bool dpiscaling = false;
#endif
		internal double dpiscale => !dpiscaling ? 1.0 : A_ScaledScreenDPI;
		internal MenuBar menuBar;
		bool marginsInit = false;
		internal nint owner = 0;
		internal Size requestedSize = new(int.MinValue, int.MinValue);
		internal Point requestedLocation = new(int.MinValue, int.MinValue);

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
				"Border", (f, o) =>
				{
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
						f.form.FormBorderStyle = f.resizable ? FormBorderStyle.Sizable : FormBorderStyle.FixedDialog;
					else
						f.form.FormBorderStyle = FormBorderStyle.None;
				}
			},
			{
				"Disabled", (f, o) => { if (o is bool b) f.form.Enabled = !b; }
			},
#if WINDOWS
			{
				"DPIScale", (f, o) => { if (o is bool b) f.dpiscaling = b; }
			},
#endif
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
					if (o is string s)
					{
						if (s?.Length == 0)
						{
							f.form.MinimumSize = new Size(-1, -1);
						}
						else if (s.EndsWith("x", StringComparison.OrdinalIgnoreCase))//Only width was specified.
						{
							if (int.TryParse(s.AsSpan(0, s.Length - 1), out var width))
								f.form.MinimumSize = new Size((int)(f.dpiscale * width), f.form.MinimumSize.Height);
						}
						else if (s.StartsWith("x", StringComparison.OrdinalIgnoreCase))//Only height was specified.
						{
							if (int.TryParse(s.AsSpan(1), out var height))
								f.form.MinimumSize = new Size(f.form.MinimumSize.Width, (int)(f.dpiscale * height));
						}
						else
						{
							var splits = s.Split('x', StringSplitOptions.RemoveEmptyEntries);

							if (splits.Length == 2)
							{
								if (int.TryParse(splits[0], out var width) && int.TryParse(splits[1], out var height))
									f.form.MinimumSize = f.dpiscaling ? new Size((int)(f.dpiscale * width), (int)(f.dpiscale * height)) : new Size(width, height);
							}
						}
					}
				}
			},
			{
				"MaxSize", (f, o) =>
				{
					if (o is string s)
					{
						if (s?.Length == 0)
						{
							f.form.MaximumSize = new Size(-1, -1);
						}
						else if (s.EndsWith("x", StringComparison.OrdinalIgnoreCase))//Only width was specified.
						{
							if (int.TryParse(s.AsSpan(0, s.Length - 1), out var width))
								f.form.MaximumSize = new Size((int)(f.dpiscale * width), f.form.MaximumSize.Height);
						}
						else if (s.StartsWith("x", StringComparison.OrdinalIgnoreCase))//Only height was specified.
						{
							if (int.TryParse(s.AsSpan(1), out var height))
								f.form.MaximumSize = new Size(f.form.MaximumSize.Width, (int)(f.dpiscale * height));
						}
						else
						{
							var splits = s.Split('x', StringSplitOptions.RemoveEmptyEntries);

							if (splits.Length == 2)
							{
								if (int.TryParse(splits[0], out var width) && int.TryParse(splits[1], out var height))
									f.form.MaximumSize = f.dpiscaling ? new Size((int)(f.dpiscale * width), (int)(f.dpiscale * height)) :new Size(width, height);
							}
						}
					}
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
					if (o is string s)
					{
						if (int.TryParse(s, out var hwnd))
						{
							f.owner = hwnd;
#if !WINDOWS
							if (Forms.Control.FromHandle(new nint(hwnd)) is Form theform)
								f.form.Owner = theform;
#endif
						}
					}
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
						f.form.FormBorderStyle = b ? FormBorderStyle.Sizable : FormBorderStyle.FixedDialog;
						f.form.SizeGripStyle = b ? SizeGripStyle.Show : SizeGripStyle.Hide;
						if (b)
							f.form.AutoSize = false;
					}
				}
			},
			{
				"SysMenu", (f, o) => { if (o is bool b) f.form.ControlBox = b; }
			},
			{
				"Theme", (f, o) => _ = KeysharpEnhancements.OutputDebugLine("Themes are not supported", false)
			},
			{
				"ToolWindow", (f, o) => {
					if (o is bool b && b)
					{
						if (f.form.FormBorderStyle != FormBorderStyle.None)//Only change border if they haven't requested that there be no border.
							f.form.FormBorderStyle = f.resizable ? FormBorderStyle.SizableToolWindow : FormBorderStyle.FixedToolWindow;

						f.form.ShowInTaskbar = false;
					}
					else
					{
						if (f.form.FormBorderStyle != FormBorderStyle.None)
							f.form.FormBorderStyle = f.resizable ? FormBorderStyle.Sizable : FormBorderStyle.FixedDialog;

						f.form.ShowInTaskbar = true;
					}
				}
			}
		};

		private bool lastfound = false;
		private bool owndialogs = false;
		private bool resizable = false;
		private bool showCalled = false;

		public object BackColor
		{
			get => (form.BackColor.ToArgb() & 0x00FFFFFF).ToString("X6");

			set
			{
				if (value is string s)
				{
					if (Conversions.TryParseColor(s, out var c))
					{
						form.BackColor = c;
						return;
					}
				}
				else if (value is long ll) {
					form.BackColor = Color.FromArgb((int)(ll | 0xFF000000));
					return;
				}
				_ = Errors.ValueErrorOccurred($"Invalid background color {value}");
			}
		}

		public Forms.Control FocusedCtrl => form.ActiveControl;

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

		public MenuBar MenuBar
		{
			get => menuBar;

			set
			{
				menuBar = value;
#if WINDOWS
				form.TagAndAdd(menuBar.MenuStrip);
				form.MainMenuStrip = menuBar.MenuStrip;
#else
				menuBar.MenuStrip.SyncEtoMenuBar();
				form.Menu = menuBar.MenuStrip.EtoMenuBar;
				form.MainMenuStrip = menuBar.MenuStrip;
#endif
			}
		}

		public object Name
		{
			get => form.Name;
			set => form.Name = value.ToString();
		}

		public KeysharpForm ParentForm => form.FindParent<KeysharpForm>();

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

		internal Forms.Control Section { get; set; }

		internal StatusStrip StatusStrip { get; set; }

		public Gui(params object[] args) : base(args) { }

		internal Gui(object obj0 = null, object obj1 = null, object obj2 = null, object obj3 = null) : base(skipLogic:true)//The last parameter is hidden and is only for internal use for when we wrap the main window in a Gui object.
		{
			var script = Script.TheScript;

			if (obj3 is KeysharpForm kf)
			{
				form = kf;

				foreach (var ctrl in form.GetAllControlsRecursive<Forms.Control>())//In order for searches that use allGuiHwnds, we must make all of the child controls point here.
					ctrl.Tag = new Gui.Control(this, ctrl, ctrl.Name, true);//Supposed to be name like "label", "edit" etc, but just pass the name since this is only used with the main window.
			}
#if !WINDOWS
			form.Content = new PixelLayout();
#endif
			LastContainer = form;
			script.GuiData.allGuiHwnds[form.Handle.ToInt64()] = this;//Calling handle forces the creation of the window.

			if (lastfound)
				script.HwndLastUsed = Hwnd;
		}

		~Gui()
		{
			Script.TheScript.ExitIfNotPersistent();//May not be necessary, but try anyway.
		}

		public IFuncObj __Enum(object count) => (new GuiControlIterator(controls, count.Ai())).fo;

		public override object __New(params object[] obj)
		{
			if (form == null)//Don't allow derived classes to init twice.
			{
				var options = obj.Length > 0 ? obj[0].As() : null;
				var caption = obj.Length > 1 ? obj[1].As() : null;
				var eventObj = obj.Length > 2 ? obj[2] : null;
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

				script.GuiData.allGuiHwnds[form.Handle.ToInt64()] = this;//Calling handle forces the creation of the window.

				if (lastfound)
					script.HwndLastUsed = Hwnd;
			}

			return DefaultObject;
		}

		void EnsureDefaultMargins()
		{
			if (marginsInit) return;
			float dpi = dpiscaling ? (float)A_ScreenDPI : 96f;
			float dpiinv = 96F / dpi;
			float fh = form.Font.GetHeight(dpi) * dpiinv;
			int mx = (int)Math.Ceiling(fh * 1.25f);
			int my = (int)Math.Ceiling(fh * 0.75f);
			form.Margin = new Padding(mx, my, mx, my);
			marginsInit = true;
		}

		public object Add(object obj0, object obj1 = null, object obj2 = null)
		{
			EnsureDefaultMargins();
			var typeo = obj0.As();
			var options = obj1.As();
			var o = obj2;//The third argument needs to account for being an array in the case of combo/list boxes.
			var type = typeo.ToLowerInvariant();
			Control holder = null;
			var text = o as string;

			if (text != null)
				text = text.ReplaceLineEndings(Environment.NewLine);

			var al = o as Array;
			var dpiscale = !dpiscaling ? 1.0 : A_ScaledScreenDPI;
			var dpiinv = 1.0 / dpiscale;
			var opts = ParseOpt(type, text, options);
			Forms.Control ctrl = null;

			switch (type)
			{
				case Keyword_Text:
				{
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
					if (obj2 != null)
						nud.Value = (decimal)obj2.Ad();
					else
						nud.Value = Math.Min(nud.Minimum, 0m);
#else
					if (obj2 != null)
						nud.Value = obj2.Ad();
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
					var pic = new KeysharpPictureBox(text, opts.addstyle, opts.addexstyle, opts.remstyle, opts.remexstyle)
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
						Name = text,
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
					rad.Text = text;
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
					else if (opts.height != int.MinValue)
					{
						ddl.IntegralHeight = false;
						ddl.DropDownHeight = opts.height;
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
					var linklabel = new KeysharpLinkLabel(text, opts.addstyle, opts.addexstyle, opts.remstyle, opts.remexstyle)
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

					if (!string.IsNullOrEmpty(text))
						hk.Text = text;

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
					dtp.SetFormat(text);

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
						slider.Value = (int)Script.ForceLong(o);

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
							.Select(text => new TabPage
							{
								Text = text,
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
					ss.ImageScalingSize = new Size((int)Math.Round(28 * dpiscale), (int)Math.Round(28 * dpiscale));
					ss.Dock = DockStyle.Bottom;//Docking must be used and must be on the bottom. Don't ever set form.AutoSize = true with this, they are incompatible.
					ss.SizingGrip = false;
					if (opts.bgcolor.HasValue)
						ss.BackColor = opts.bgcolor.Value;

					if (!string.IsNullOrEmpty(text))
					{
						var tsl = new KeysharpToolStripStatusLabel(text)
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
					var ax = new KeysharpActiveX(text)
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
					web.Navigate(text);
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
				return DefaultErrorObject;

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

			if (text != null && ctrl is not KeysharpDateTimePicker && ctrl is not HotkeyBox
#if WINDOWS
			 && ctrl is not KeysharpLinkLabel
#endif
			 )
				ctrl.Text = text;

			if (ctrl is not KeysharpStatusStrip)//Don't want status strip to have a margin, so it can be placed at the bottom of the form when autosize is true, and have it look exactly like it would if it were docked when autosize is false.
				ctrl.Margin = form.Margin;

			//ctrl.Padding = form.Padding;

			if (opts.visible.HasValue)
				ctrl.Visible = opts.visible.Value;

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

			controls[ctrl.Handle.ToInt64()] = holder;
			var prevParent = LastContainer;

			if (opts.altsubmit.HasValue)
				holder.AltSubmit = opts.altsubmit.Value;

			var lastControl = LastControl;

			if (lastControl is KeysharpRadioButton && (ctrl is not KeysharpRadioButton || opts.group))//Pop container if we've ended a radio group.
			{
				LastContainer = LastContainer.GetLogicalParent();
				lastControl = LastControl;//Will retrieve the last control in the LastContainer we just assigned.
			}

			var isNewRadioGroup = ctrl is KeysharpRadioButton && (lastControl == null || lastControl is not KeysharpRadioButton || opts.group);
			var isTabControl = ctrl is KeysharpTabControl;
			var rbContainer = lastControl?.GetLogicalParent() ?? LastContainer;
			bool xSpecified = opts.xpos != GuiOptions.Positioning.None;
			bool ySpecified = opts.ypos != GuiOptions.Positioning.None;
			bool needsContainerMostForPositioning =
				(!ySpecified && (opts.xpos == GuiOptions.Positioning.Absolute || opts.xpos == GuiOptions.Positioning.Container || opts.xpos == GuiOptions.Positioning.Margin))
				|| (!xSpecified && (opts.ypos == GuiOptions.Positioning.Absolute || opts.ypos == GuiOptions.Positioning.Container || opts.ypos == GuiOptions.Positioning.Margin));
			bool needsSectionMostForPositioning =
				Section?.Parent != null
				&& ((!ySpecified && opts.xpos == GuiOptions.Positioning.Section) || (!xSpecified && opts.ypos == GuiOptions.Positioning.Section));
			bool needsLastParentMostForPositioning = lastControl != null
				&& lastControl.Dock == DockStyle.None
				&& Section == null
				&& ((!ySpecified && opts.xpos == GuiOptions.Positioning.Section) || (!xSpecified && opts.ypos == GuiOptions.Positioning.Section));
			(Forms.Control right, Forms.Control bottom) rbContainerMost = needsContainerMostForPositioning && rbContainer != null
				? rbContainer.RightBottomMost()
				: (null, null);
			(Forms.Control right, Forms.Control bottom) sectionMost = needsSectionMostForPositioning
				? Section.Parent.RightBottomMostSince(Section)
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
					w = (int)Math.Round(dpiscale * opts.width);
			}
			else if (ctrl is KeysharpProgressBar kpb && ((kpb.AddStyle & 0x04) == 0x04))
				w = fontpixels * 2;
			else if (ctrl is KeysharpNumericUpDown)
				w = lcWidth;
			else if (ctrl is KeysharpComboBox || ctrl is HotkeyBox || ctrl is KeysharpListBox || ctrl is KeysharpNumericUpDown || ctrl is KeysharpProgressBar || ctrl is KeysharpTextBox || ctrl is KeysharpPasswordBox)
				w = fontpixels * 15;
			else if (ctrl is KeysharpTrackBar trk)
				w = trk.Orientation == Orientation.Horizontal ? fontpixels * 15 : fontpixels * 2;//Documentation didn't mention a default for vertical trackbars, so just make it the same a vertical progress bar.
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
			finalWidth = opts.width == int.MinValue && opts.wp == int.MinValue ? Math.Max((int)w, (int)Math.Round(scaledPref)) : (holder.requestedSize.Width = (int)Math.Round(w));

			if (opts.hp != int.MinValue)
			{
				var ctrlSize = ctrl.GetSize();
				var newHeight = lastControl != null ? lcHeight + (int)(opts.hp * dpiscale) : 0;
				ctrl.SetSize(new Size(ctrlSize.Width, newHeight));
			}
			else
			{
				if (opts.height != int.MinValue)
				{
					if (opts.height != -1)
						finalHeight = holder.requestedSize.Height = (int)Math.Round(dpiscale * opts.height);
				}
				else
				{
					var r = 0;

					if (opts.rows != float.MinValue)
						r = (int)Math.Round(opts.rows);
					else if (ctrl is KeysharpComboBox || ctrl is KeysharpListBox)
						r = 3;
					else if (ctrl is KeysharpListView || ctrl is KeysharpTreeView || (ctrl is KeysharpProgressBar kpb2 && ((kpb2.AddStyle & 0x04) == 0x04)))
						r = 5;
					else if (ctrl is KeysharpGroupBox)
						r = 2;
					else if (ctrl is KeysharpTextBox tb)
						r = tb.Multiline ? 3 : 1;
					else if (ctrl is KeysharpPasswordBox)
						r = 1;
					else if (ctrl is KeysharpDateTimePicker || ctrl is HotkeyBox || ctrl is KeysharpProgressBar)
						r = 1;
					else if (ctrl is TabPage || ctrl is KeysharpTabControl)
						r = 10;

#if WINDOWS
					else if (ctrl is KeysharpCustomControl custom)
						r = 5;//AHK used 5.

#endif
					var fontRows = (int)(Math.Round(fontpixels + 0.5) * r);//This is a rough attempt to make text boxes tall enough to show the requested number of lines without having the scrollbars appear unnecessarily.
					var defheight = fontRows;//AHK used external leading, but just use fontpixels here because it's close enough.

					if (ctrl is KeysharpComboBox cmb)
					{
						cmb.MaxDropDownItems = r;
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
						finalHeight = defheight;
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
						finalHeight = defheight + (int)Math.Round((tc2.Margin.Top + tc2.Margin.Bottom) *  (2.0 + ((int)(r + 1.5) - 1)));//Same here, but -1.
					}

#if WINDOWS
					else if (ctrl is KeysharpCustomControl)
					{
						finalHeight = fontRows + ctrl.Margin.Top;
					}

#endif
					else
					{
						if (opts.rows == float.MinValue) //Neither r or h were specified.
						{
							if (ctrl is KeysharpTrackBar trk && opts.thick == int.MinValue)//Separate check for TrackBar because the documentation specifies it in pixels. Skip this if thickness has been specified.
							{
								finalHeight = trk.Orientation == Orientation.Horizontal ? 30 : (int)Math.Round(5 * fontpixels);
								goto heightdone;
							}
							else if (ctrl is KeysharpLabel lbl)
							{
								bool hasW = opts.width != int.MinValue || opts.wp != int.MinValue;
								bool hasH = opts.height != int.MinValue || opts.hp != int.MinValue;

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
									lbl.AutoSize = true;
								}
								goto heightdone;
							}
						}

						if (r > 0)
						{
							finalHeight = finalHeight - ctrl.ClientSize.Height + fontRows;
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
			ctrl.SetSize(new Size(finalWidth, finalHeight));
			Point loc;

			var xoffset = (double)lcLeft;
			var yoffset = (double)lcTop;

			if (opts.xpos == GuiOptions.Positioning.Absolute || opts.ypos == GuiOptions.Positioning.Absolute
				|| opts.xpos == GuiOptions.Positioning.Margin || opts.ypos == GuiOptions.Positioning.Margin)
			{
				Point p = LastContainer?.GetLocationRelativeToForm() ?? Point.Empty;

				if (opts.xpos == GuiOptions.Positioning.Absolute || opts.xpos == GuiOptions.Positioning.Margin)
					xoffset = p.X; 
				if (opts.ypos == GuiOptions.Positioning.Absolute || opts.ypos == GuiOptions.Positioning.Margin)
					yoffset = p.Y;
			}

			if (opts.xpos == GuiOptions.Positioning.Absolute)
				xoffset = opts.x * dpiscale - xoffset;
			else if (opts.xpos == GuiOptions.Positioning.PreviousBottomRight)
				xoffset += lcWidth + (opts.x * dpiscale);
			else if (opts.xpos == GuiOptions.Positioning.PreviousTopLeft)
				xoffset += opts.x * dpiscale;
			else if (opts.xpos == GuiOptions.Positioning.Margin)
				xoffset = form.Margin.Left + (opts.x * dpiscale) - xoffset;
			else if (opts.xpos == GuiOptions.Positioning.Section)
				xoffset = (Section?.GetLocation().X ?? 0) + (opts.x * dpiscale);
			else if (opts.xpos == GuiOptions.Positioning.Container)
				xoffset = opts.x * dpiscale;
			else
				xoffset = int.MinValue;

			if (opts.ypos == GuiOptions.Positioning.Absolute)
				yoffset = opts.y * dpiscale - yoffset;
			else if (opts.ypos == GuiOptions.Positioning.PreviousBottomRight)
				yoffset += lcHeight + (opts.y * dpiscale);
			else if (opts.ypos == GuiOptions.Positioning.PreviousTopLeft)
				yoffset += opts.y * dpiscale;
			else if (opts.ypos == GuiOptions.Positioning.Margin)
				yoffset = form.Margin.Top + (opts.y * dpiscale) - yoffset;
			else if (opts.ypos == GuiOptions.Positioning.Section)
				yoffset = (Section?.GetLocation().Y ?? 0) + (opts.y * dpiscale);
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
					var (r, b) = rbContainerMost;//Get the bottom-most control in the current container.
					var bLoc = b?.GetLocation() ?? Point.Empty;
					var bSize = b?.GetSize() ?? Size.Empty;
					yoffset = bLoc.Y + bSize.Height + form.Margin.Top;
				}
				else if (opts.xpos == GuiOptions.Positioning.Section && needsSectionMostForPositioning)//XS: Beneath all previous controls since the most recent use of the Section option.
				{
					var (r, b) = sectionMost;//Get the bottom-most control in the current section.
					var bLoc = b?.GetLocation() ?? Point.Empty;
					var bSize = b?.GetSize() ?? Size.Empty;
					yoffset = bLoc.Y + bSize.Height + form.Margin.Top;
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
					var (r, b) = rbContainerMost;//Get the right-most control in the current container.
					var rLoc = r?.GetLocation() ?? Point.Empty;
					var rSize = r?.GetSize() ?? Size.Empty;
					xoffset = rLoc.X + rSize.Width + form.Margin.Left;
				}
				else if (opts.ypos == GuiOptions.Positioning.Section && needsSectionMostForPositioning)//YS: To the right of all previous controls since the most recent use of the Section option.
				{
					var (r, b) = sectionMost;//Get the right-most control in the current section.
					var rLoc = r?.GetLocation() ?? Point.Empty;
					var rSize = r?.GetSize() ?? Size.Empty;
					xoffset = rLoc.X + rSize.Width + form.Margin.Left;
				}
			}
			else if (xoffset == int.MinValue && yoffset == int.MinValue && ctrl is KeysharpNumericUpDown)
			{
				xoffset = lcLeft; yoffset = lcTop;
			}

			loc = new Point((int)Math.Round(xoffset), (int)Math.Round(yoffset));

			//Note we check DockStyle here because if the previous control was docked to a side, then we can't really use its location as a reference to base this control's location off of.
			if (ctrl is KeysharpStatusStrip ksss)//Need to figure out how to do this without resizing all tab controls on every add. Maybe at the end before show(), and also on every add after show()?//TODO
			{
				form.UpdateStatusStripLayout();
			}
			else if (loc.X != int.MinValue && loc.Y != int.MinValue)//If both x and y were specified, that takes precedence over everything else.
			{
			}
			else if (lastControl != null && lastControl.Dock == DockStyle.None && loc.X == int.MinValue && loc.Y == int.MinValue)
			{
				loc = new Point(lcLeft, lcTop + lcHeight + form.Margin.Bottom);
			}
			else if (lastControl != null && lastControl.Dock == DockStyle.None && loc.X == int.MinValue && needsLastParentMostForPositioning)
			{
				//Will only have gotten here if y was specified in absolute coords using Yn with x omitted.
				var (r, b) = lastControlParentMost;//Get the right-most control in the current container.
				var rLoc = r?.GetLocation() ?? Point.Empty;
				var rSize = r?.GetSize() ?? Size.Empty;
				loc = new Point(rLoc.X + rSize.Width + r.Margin.Right, loc.Y);
			}
			else if (lastControl != null && lastControl.Dock == DockStyle.None && loc.Y == int.MinValue && needsLastParentMostForPositioning)//Same but for loc.X.
			{
				//Will only have gotten here if x was specified in absolute coords using Xn with y omitted.
				var (r, b) = lastControlParentMost;//Get the bottom-most control in the current container.
				var bLoc = b?.GetLocation() ?? Point.Empty;
				var bSize = b?.GetSize() ?? Size.Empty;
				loc = new Point(loc.X, bLoc.Y + bSize.Height + b.Margin.Bottom);
			}
			else//Final fallback when nothing else has worked.
			{
				var top = (double)prevParent.Margin.Top;

				if (prevParent is Form f && f.MainMenuStrip != null)
					top += f.MainMenuStrip.GetSize().Height;

				if (loc.Y == int.MinValue && LastContainer is KeysharpGroupBox gblast)
				{
					//Top needs to be manually adjusted when the container is a GroupBox, we're adding the first control, and they haven't explicitly specified a Y coordinate.
					if (lastContainerChildCount == 0)
						top += gblast.Margin.Top + gblast.Padding.Bottom;
				}

				loc = new Point(opts.x != int.MinValue ? opts.x : prevParent.Margin.Left,
										  opts.y != int.MinValue ? opts.y : (int)Math.Round(top));
			}

			if (isTabControl)
			{
				var ktc = (KeysharpTabControl)ctrl;
				if (ktc.TabPages.Count >= 0)
					holder.UseTab(1);//Will set this object's CurrentTab value, as well as the LastContainer values.

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

#if WINDOWS

			if (ctrl is KeysharpActiveX kax)
				kax.Init();

#endif

			if (ctrl is KeysharpPictureBox pbox)
			{
				if (text != null && ImageHelper.LoadImage(text, opts.width, opts.height, opts.iconnumber).Item1 is Bitmap bmp)
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

			if (form.BeenShown)//See above for a description of this logic.
			{
				//If it's been shown and is contained anywhere within the hierarchy of a TabControl, then resize
				//every TabControl in the chain.
				foreach (var partc in ctrl.FindParents<KeysharpTabControl>())
					partc.AdjustSize(dpiscale, new Size(int.MinValue, int.MinValue));
			}

			if (opts.section)
				Section = ctrl;

#if !WINDOWS
			if (form.Visible) 
			{
				form.Invalidate();
				form.UpdateLayout();
				form.Show();
			}
#endif

			return holder;
		}

		public object AddActiveX(object obj0 = null, object obj1 = null) => Add(Keyword_ActiveX, obj0, obj1);

		public object AddButton(object obj0 = null, object obj1 = null) => Add(Keyword_Button, obj0, obj1);

		public object AddCheckbox(object obj0 = null, object obj1 = null) => Add(Keyword_CheckBox, obj0, obj1);

		public object AddComboBox(object obj0 = null, object obj1 = null) => Add(Keyword_ComboBox, obj0, obj1);

		public object AddCustom(object obj0 = null, object obj1 = null) => Add(Keyword_Custom, obj0, obj1);

		public object AddDateTime(object obj0 = null, object obj1 = null) => Add(Keyword_DateTime, obj0, obj1);

		public object AddDDL(object obj0 = null, object obj1 = null) => Add(Keyword_DropDownList, obj0, obj1);

		public object AddDropDownList(object obj0 = null, object obj1 = null) => Add(Keyword_DropDownList, obj0, obj1);

		public object AddEdit(object obj0 = null, object obj1 = null) => Add(Keyword_Edit, obj0, obj1);

		public object AddGroupBox(object obj0 = null, object obj1 = null) => Add(Keyword_GroupBox, obj0, obj1);

		public object AddHotKey(object obj0 = null, object obj1 = null) => Add(Keyword_Hotkey, obj0, obj1);

		public object AddLink(object obj0 = null, object obj1 = null) => Add(Keyword_Link, obj0, obj1);

		public object AddListBox(object obj0 = null, object obj1 = null) => Add(Keyword_ListBox, obj0, obj1);

		public object AddListView(object obj0 = null, object obj1 = null) => Add(Keyword_ListView, obj0, obj1);

		public object AddMonthCal(object obj0 = null, object obj1 = null) => Add(Keyword_MonthCal, obj0, obj1);

		public object AddPic(object obj0 = null, object obj1 = null) => Add(Keyword_Picture, obj0, obj1);

		public object AddPicture(object obj0 = null, object obj1 = null) => Add(Keyword_Picture, obj0, obj1);

		public object AddProgress(object obj0 = null, object obj1 = null) => Add(Keyword_Progress, obj0, obj1);

		public object AddRadio(object obj0 = null, object obj1 = null) => Add(Keyword_Radio, obj0, obj1);

		public object AddSlider(object obj0 = null, object obj1 = null) => Add(Keyword_Slider, obj0, obj1);

		public object AddStatusBar(object obj0 = null, object obj1 = null) => Add(Keyword_StatusBar, obj0, obj1);

		public object AddTab(object obj0 = null, object obj1 = null) => Add(Keyword_Tab, obj0, obj1);
		// Just for compatibility
		public object AddTab2(object obj0 = null, object obj1 = null) => Add(Keyword_Tab, obj0, obj1);
		public object AddTab3(object obj0 = null, object obj1 = null) => Add(Keyword_Tab, obj0, obj1);

		public object AddText(object obj0 = null, object obj1 = null) => Add(Keyword_Text, obj0, obj1);

		public object AddTreeView(object obj0 = null, object obj1 = null) => Add(Keyword_TreeView, obj0, obj1);

		public object AddUpDown(object obj0 = null, object obj1 = null) => Add(Keyword_UpDown, obj0, obj1);

		public object AddWebBrowser(object obj0 = null, object obj1 = null) => Add(Keyword_WebBrowser, obj0, obj1);

		public object Destroy() => form?.Destroy();

		public object Flash(object obj)
		{
#if WINDOWS
			_ = WindowsAPI.FlashWindow(form.Handle, obj.Ab(true));
#endif
			return DefaultObject;
		}

		public object GetClientPos([Optional()][DefaultParameterValue(null)] object outX,
								   [Optional()][DefaultParameterValue(null)] object outY,
								   [Optional()][DefaultParameterValue(null)] object outWidth,
								   [Optional()][DefaultParameterValue(null)] object outHeight)
		{
			Gui.Control.GetClientPos(form, dpiscaling, outX, outY, outWidth, outHeight);
			return DefaultObject;
		}

		public IEnumerator<(object, object)> GetEnumerator() => new GuiControlIterator(controls, 2);

		public object GetPos([Optional()][DefaultParameterValue(null)] object outX, [Optional()][DefaultParameterValue(null)] object outY, [Optional()][DefaultParameterValue(null)] object outWidth, [Optional()][DefaultParameterValue(null)] object outHeight)
		{
			Gui.Control.GetPos(form, dpiscaling, outX, outY, outWidth, outHeight);
			return DefaultObject;
		}

		public object Hide()
		{
			form.Hide();
			return DefaultObject;
		}

		public object Maximize() => form.WindowState = FormWindowState.Maximized;

		public object Minimize() => form.WindowState = FormWindowState.Minimized;

		public object Move(object obj0 = null, object obj1 = null, object obj2 = null, object obj3 = null)
		{
			var x = obj0.Ai(int.MinValue);
			var y = obj1.Ai(int.MinValue);
			var width = obj2.Ai(int.MinValue);
			var height = obj3.Ai(int.MinValue);
			var scale = !dpiscaling ? 1.0 : A_ScaledScreenDPI;
			var formLoc = form.GetLocation();
			var formSize = form.GetSize();

			if (x != int.MinValue)
				formLoc.X = x;

			if (y != int.MinValue)
				formLoc.Y = y;

			if (width != int.MinValue)
				formSize.Width = (int)Math.Round(width * scale);

			if (height != int.MinValue)
				formSize.Height = (int)Math.Round(height * scale);

			if (x != int.MinValue || y != int.MinValue)
				form.SetLocation(formLoc);
			if (width != int.MinValue || height != int.MinValue)
				form.SetSize(formSize);

			return DefaultObject;
		}

		public object OnEvent(object obj0, object obj1, object obj2 = null) => form.OnEvent(obj0, obj1, obj2);

		public object Opt(object obj)
		{
			var options = obj.As();

			foreach (var split in Options.ParseOptions(options))
			{
				var str = split.Substring(1);

				if (str.Length > 0)
				{
					var val = "";

					if (str.StartsWith("MinSize", StringComparison.OrdinalIgnoreCase))
					{
						if (split[0] == '+')
							val = str.Substring(7);

						if (showOptionsDkt.TryGetValue("MinSize", out var func))
							func(this, val);
					}
					else if (str.StartsWith("MaxSize", StringComparison.OrdinalIgnoreCase))
					{
						if (split[0] == '+')
							val = str.Substring(7);

						if (showOptionsDkt.TryGetValue("MaxSize", out var func))
							func(this, val);
					}
					else if (str.StartsWith("Owner", StringComparison.OrdinalIgnoreCase))
					{
						if (split[0] == '+')
							val = str.Substring(5);

						if (showOptionsDkt.TryGetValue("Owner", out var func))
							func(this, val);
					}
					else if (str.StartsWith("Parent", StringComparison.OrdinalIgnoreCase))
					{
						if (split[0] == '+')
							val = str.Substring(6);

						if (showOptionsDkt.TryGetValue("Owner", out var func))
							func(this, val);
					}
					else if (showOptionsDkt.TryGetValue(str, out var func))//Used +/-.
					{
						if (split[0] == '+')
							func(this, true);
						else if (split[0] == '-')
							func(this, false);
					}
					else if (showOptionsDkt.TryGetValue(split, out var func2))//No +/-, so just try as is.
					{
						func2(this, true);//No +/- so just assume true.
					}
				}
			}

			return DefaultObject;
		}

		public object Restore()
		{
			if (!form.Visible)
				form.Show();
			form.WindowState = FormWindowState.Normal;
			return DefaultObject;
		}

		public object SetFont(object obj0 = null, object obj1 = null)
		{
			form.SetFont(obj0, obj1);
			return DefaultObject;
		}

		public object Show(object obj = null)
		{
			EnsureDefaultMargins();
			var s = obj.As();
			bool /*center = false, cX = false, cY = false,*/ auto = false, min = false, max = false, restore = true, hide = false, cX = false, cY = false;
			var dpiscale = !dpiscaling ? 1.0 : A_ScaledScreenDPI;

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
			var status = form.GetControls().OfType<KeysharpStatusStrip>().ToArray();
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

				form.ClientSize = size;
			}

#if WINDOWS
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
				form.SetLocation(location);

			if (hide)
				form.Hide();
#if WINDOWS
			else if (!form.BeenShown && owner != 0)
			{
				form.Show(new WindowItem(owner));
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
#if WINDOWS
			form.Update();//Required for the very first state of the form to always be displayed.
#endif
			return DefaultObject;
		}

        public KeysharpObject Submit(object obj = null)
		{
			var hide = ForceBool(obj ?? true);
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

			if (hide)
				_ = Hide();

			return result;
		}

		public object UseGroup(object obj0 = null)
		{
			if (obj0 is Gui.Control gctrl && gctrl.Ctrl is KeysharpGroupBox gb)
				LastContainer = gb;
			else
				LastContainer = form;

			return DefaultObject;
		}

		IEnumerator IEnumerable.GetEnumerator() => new GuiControlIterator(controls, 2);

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

		internal GuiOptions ParseOpt(string type, string text, string optionsstr)
		{
			var options = new GuiOptions();

			if (type == "monthcal" && !string.IsNullOrEmpty(text))
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
				var tempbool = false;
				var temp = 0;
				var tempcolor = Color.Empty;
				var tempstr = "";
				var opt = optionsstr.AsSpan(r).Trim();

				if (opt.Length > 0)
				{
					if (type == "datetime")
					{
						if (Options.TryParseDateTime(opt, "Choose", "yyyyMMdd", ref options.dtChoose)) { continue; }
						else if (opt.Equals("ChooseNone", StringComparison.OrdinalIgnoreCase)) { options.choosenone = true; continue; }
						else if (opt == "1") { options.dtopt1 = true; continue; }
						else if (opt == "2") { options.dtopt2 = true; continue; }
					}
					else if (type == "monthcal")
					{
						if (Options.TryParse(opt, "Multi", ref tempbool, StringComparison.OrdinalIgnoreCase, true, true)) { options.datemultisel = true; continue; }
					}
					else
					{
						if (Options.TryParse(opt, "Multi", ref tempbool, StringComparison.OrdinalIgnoreCase, true, true)) { options.multiline = tempbool; continue; }
					}

					if (Options.TryParse(opt, "r", ref options.rows)) { }
					else if (Options.TryParse(opt, "w", ref options.width)) { }
					else if (Options.TryParse(opt, "h", ref options.height)) { }
					else if (Options.TryParse(opt, "x+", ref options.x)) { options.xpos = GuiOptions.Positioning.PreviousBottomRight; }
					else if (Options.TryParse(opt, "y+", ref options.y)) { options.ypos = GuiOptions.Positioning.PreviousBottomRight; }
					//else if (string.Compare(opt, "x+m", true) == 0) { options.xplusm = true; }
					//else if (string.Compare(opt, "y+m", true) == 0) { options.yplusm = true; }
					else if (Options.TryParse(opt, "x", ref options.x)) { options.xpos = GuiOptions.Positioning.Absolute; }
					else if (Options.TryParse(opt, "y", ref options.y)) { options.ypos = GuiOptions.Positioning.Absolute; }
					else if (Options.TryParse(opt, "t", ref options.t)) { options.tabstops.Add(options.t); }
					else if (Options.TryParse(opt, "Redraw", ref tempbool, StringComparison.OrdinalIgnoreCase, true, true)) { options.redraw = tempbool; }
					//Checkbox.
					else if (opt.Equals("Check3", StringComparison.OrdinalIgnoreCase)) { options.check3 = true; }//Needs to come before any option starting with a 'c'.
					else if (opt.Equals("CheckedGray ", StringComparison.OrdinalIgnoreCase)) { options.checkedgray = true; }
					else if (Options.TryParse(opt, "Checked", ref temp, StringComparison.OrdinalIgnoreCase, true, 1)) { options.ischecked = temp; }
					else if (Options.TryParseString(opt, "Range", ref options.nudrange))
					{
						if (type == "datetime" || type == "monthcal")
						{
							Conversions.ParseRange(options.nudrange, out options.dtlow, out options.dthigh);
						}
						else if (type == "updown" || type == "slider" || type == "progress")
						{
							var splits = options.nudrange.Split('-', StringSplitOptions.None);
							var vals = Conversions.ParseRange(splits);

							if (vals.Count > 0)
								options.nudlow = vals[0];

							if (vals.Count > 1)
								options.nudhigh = vals[1];
						}
					}
					else if (Options.TryParse(opt, "Choose", ref options.ddlchoose)) { options.ddlchoose--; options.choose.Add(options.ddlchoose); }
					//
					else if (Options.TryParse(opt, "Vertical", ref tempbool, StringComparison.OrdinalIgnoreCase, true, true)) { options.vertical = tempbool; }
					else if (Options.TryParseString(opt, "v", ref options.name)) { }
					else if (Options.TryParse(opt, "Disabled", ref tempbool, StringComparison.OrdinalIgnoreCase, true, true)) { options.enabled = !tempbool; }
					else if (Options.TryParse(opt, "Hidden", ref tempbool, StringComparison.OrdinalIgnoreCase, true, true)) { options.visible = !tempbool; }
					else if (Options.TryParse(opt, "Autosize", ref tempbool, StringComparison.OrdinalIgnoreCase, true, true)) { options.autosize = tempbool; }
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
					else if (Options.TryParse(opt, "AltSubmit", ref tempbool, StringComparison.OrdinalIgnoreCase, true, true)) { options.altsubmit = tempbool; }
					else if (opt.Equals("Section", StringComparison.OrdinalIgnoreCase)) { options.section = true; }
					else if (Options.TryParse(opt, "Tabstop", ref tempbool, StringComparison.OrdinalIgnoreCase, true, true)) { options.tabstop = tempbool; }
					else if (Options.TryParse(opt, "Wrap", ref tempbool, StringComparison.OrdinalIgnoreCase, true, true)) { options.wordwrap = tempbool; }
					else if (Options.TryParse(opt, "VScroll", ref tempbool, StringComparison.OrdinalIgnoreCase, true, true)) { options.vscroll = tempbool; }
					else if (opt.Equals("-HScroll", StringComparison.OrdinalIgnoreCase)) { options.hscroll = false; }
					else if (Options.TryParse(opt, "HScroll", ref options.hscrollamt, StringComparison.OrdinalIgnoreCase, true)) { }
					else if (Options.TryParse(opt, "Increment", ref temp)) { options.nudinc = temp; }
					else if (Options.TryParse(opt, "Hex", ref tempbool, StringComparison.OrdinalIgnoreCase, true, true)) { options.hex = tempbool; }
					else if (opt.Equals("BackgroundTrans", StringComparison.OrdinalIgnoreCase))
					{
						options.addexstyle |= 0x00000020;
						options.bgtrans = true;
					}
					else if (opt.Equals("-Background", StringComparison.OrdinalIgnoreCase)) { options.bgcolor = Forms.Control.DefaultBackColor; }
					else if (opt.Equals("Background", StringComparison.OrdinalIgnoreCase)) { options.bgcolor = Forms.Control.DefaultBackColor; }
					else if (opt.Equals("BackgroundDefault", StringComparison.OrdinalIgnoreCase)) { options.bgcolor = Forms.Control.DefaultBackColor; }
					else if (Options.TryParse(opt, "Background", ref tempcolor, StringComparison.OrdinalIgnoreCase, true)) { options.bgcolor = tempcolor; }
					else if (Options.TryParse(opt, "Border", ref tempbool, StringComparison.OrdinalIgnoreCase, true, true)) { options.thinborder = tempbool; }

					else if (opt.Equals("Left", StringComparison.OrdinalIgnoreCase)) { options.halign = GuiOptions.HorizontalAlignment.Left; }
					else if (opt.Equals("Center", StringComparison.OrdinalIgnoreCase)) { options.halign = GuiOptions.HorizontalAlignment.Center; }
					else if (opt.Equals("Right", StringComparison.OrdinalIgnoreCase)) { options.halign = GuiOptions.HorizontalAlignment.Right; }
					else if (opt.Equals("Bottom", StringComparison.OrdinalIgnoreCase)) { options.valign = GuiOptions.VerticalAlignment.Bottom; }
					else if (opt.Equals("Top", StringComparison.OrdinalIgnoreCase)) { options.valign = GuiOptions.VerticalAlignment.Top; }
					else if (opt.Equals("Middle", StringComparison.OrdinalIgnoreCase)) { options.valign = GuiOptions.VerticalAlignment.Middle; }

					//Control specific.
					//Edit.
					else if (Options.TryParse(opt, "limit", ref options.limit, StringComparison.OrdinalIgnoreCase, true)) { }
					else if (Options.TryParse(opt, "Lowercase", ref tempbool, StringComparison.OrdinalIgnoreCase, true, true)) { options.lowercase = tempbool; }
					else if (Options.TryParse(opt, "Uppercase", ref tempbool, StringComparison.OrdinalIgnoreCase, true, true)) { options.uppercase = tempbool; }
					else if (Options.TryParse(opt, "Number", ref tempbool, StringComparison.OrdinalIgnoreCase, true, true)) { options.number = tempbool; }
					else if (Options.TryParseString(opt, "Password", ref options.pwdch, StringComparison.OrdinalIgnoreCase)) { options.pwd = true; }
					else if (Options.TryParse(opt, "ReadOnly", ref tempbool, StringComparison.OrdinalIgnoreCase, true, true)) { options.rdonly = tempbool; }
					else if (Options.TryParse(opt, "WantCtrlA", ref tempbool, StringComparison.OrdinalIgnoreCase, true, true)) { options.wantctrla = tempbool; }
					else if (Options.TryParse(opt, "WantReturn", ref tempbool, StringComparison.OrdinalIgnoreCase, true, true)) { options.wantreturn = tempbool; }
					else if (Options.TryParse(opt, "WantTab", ref tempbool, StringComparison.OrdinalIgnoreCase, true, true)) { options.wanttab = tempbool; }
					//GrouBox.
					else if (opt.Equals("Group", StringComparison.OrdinalIgnoreCase)) { options.group = true; }
					//UpDown.
					else if (opt.Equals("Horz", StringComparison.OrdinalIgnoreCase)) { options.nudhorz = true; }
					//16
					//0x80
					//None unit inc/dec
					//Button.
					else if (Options.TryParse(opt, "Default", ref tempbool, StringComparison.OrdinalIgnoreCase, true, true)) { options.btndef = tempbool; }
					//DropDownList.
					else if (Options.TryParse(opt, "Sort", ref tempbool, StringComparison.OrdinalIgnoreCase, true, true)) { options.sort = tempbool; }
					//ComboBox.
					else if (Options.TryParse(opt, "Simple", ref tempbool, StringComparison.OrdinalIgnoreCase, true, true)) { options.cmbsimple = tempbool; }
					else if (Options.TryParse(opt, "Invert", ref tempbool, StringComparison.OrdinalIgnoreCase, true, true)) { options.invert = tempbool; }
					else if (Options.TryParse(opt, "Line", ref options.line)) { }
					else if (Options.TryParse(opt, "NoTicks", ref tempbool, StringComparison.OrdinalIgnoreCase, true, true)) { options.noticks = tempbool; }
					else if (Options.TryParse(opt, "Page", ref options.page)) { }
					else if (Options.TryParse(opt, "Thick", ref options.thick)) { }
					else if (Options.TryParse(opt, "TickInterval", ref options.tickinterval)) { }
					else if (opt.Equals("ToolTip", StringComparison.OrdinalIgnoreCase)) { options.tooltip = true; }
					else if (opt.Equals("ToolTipTop", StringComparison.OrdinalIgnoreCase)) { options.tooltipside = 0; }
					else if (opt.Equals("ToolTipLeft", StringComparison.OrdinalIgnoreCase)) { options.tooltipside = 1; }
					else if (opt.Equals("ToolTipBottom", StringComparison.OrdinalIgnoreCase)) { options.tooltipside = 2; }
					else if (opt.Equals("ToolTipRight", StringComparison.OrdinalIgnoreCase)) { options.tooltipside = 3; }
					else if (Options.TryParse(opt, "Smooth", ref tempbool, StringComparison.OrdinalIgnoreCase, true, true)) { options.smooth = tempbool; }
					else if (Options.TryParse(opt, "Buttons", ref tempbool, StringComparison.OrdinalIgnoreCase, true, true)) { options.buttons = tempbool; }
					else if (Options.TryParse(opt, "ImageList", ref options.ilid)) { }
					else if (Options.TryParse(opt, "Lines", ref tempbool, StringComparison.OrdinalIgnoreCase, true, true)) { options.lines = tempbool; }
					else if (Options.TryParse(opt, "WantF2", ref tempbool, StringComparison.OrdinalIgnoreCase, true, true)) { options.wantf2 = tempbool; }
					//ListView.
					else if (Options.TryParse(opt, "SortDesc", ref tempbool, StringComparison.OrdinalIgnoreCase, true, true)) { options.sortdesc = tempbool; }
					else if (Options.TryParse(opt, "Grid", ref tempbool, StringComparison.OrdinalIgnoreCase, true, true)) { options.grid = tempbool; }
					else if (Options.TryParse(opt, "Hdr", ref tempbool, StringComparison.OrdinalIgnoreCase, true, true)) { options.header = tempbool; }
					else if (Options.TryParse(opt, "NoSortHdr", ref tempbool, StringComparison.OrdinalIgnoreCase, true, true)) { options.clickheader = !tempbool; }
					else if (Options.TryParse(opt, "NoSort", ref tempbool, StringComparison.OrdinalIgnoreCase, true, true)) { options.sortheader = !tempbool; }
					else if (Options.TryParse(opt, "Icon", ref tempbool, StringComparison.OrdinalIgnoreCase, true, true)) { if (tempbool) options.lvview = View.LargeIcon; }
					else if (Options.TryParse(opt, "Tile", ref tempbool, StringComparison.OrdinalIgnoreCase, true, true)) { if (tempbool) options.lvview = View.Tile; }
					else if (Options.TryParse(opt, "IconSmall", ref tempbool, StringComparison.OrdinalIgnoreCase, true, true)) { if (tempbool) options.lvview = View.SmallIcon; }
					else if (Options.TryParse(opt, "List", ref tempbool, StringComparison.OrdinalIgnoreCase, true, true)) { if (tempbool) options.lvview = View.List; }
					else if (Options.TryParse(opt, "Report", ref tempbool, StringComparison.OrdinalIgnoreCase, true, true)) { if (tempbool) options.lvview = View.Details; }
					//PictureBox.
					else if (Options.TryParseString(opt, "Icon", ref tempstr)) { options.iconnumber = ImageHelper.PrepareIconNumber(tempstr); }
					//Other.
					else if (Options.TryParse(opt, "c", ref tempcolor)) { options.c = tempcolor; }
					else if (opt == "4") { options.opt4 = true; }
					else if (opt == "8") { options.opt8 = true; }
					else if (opt == "16") { options.opt16 = true; }
					else if (Options.TryParse(opt, "+E", ref options.addexstyle)) { }
					else if (Options.TryParse(opt, "E", ref options.addexstyle)) { }
					else if (Options.TryParse(opt, "-E", ref options.remexstyle)) { }
					else if (Options.TryParse(opt, "+LV", ref temp)) { options.addlvstyle |= temp; }
					else if (Options.TryParse(opt, "LV", ref temp)) { options.addlvstyle |= temp; }
					else if (Options.TryParse(opt, "-LV", ref temp)) { options.remlvstyle &= ~temp; }
					else if (Options.TryParse(opt, "-", ref options.remstyle)) { }
					else if (Options.TryParse(opt, "+", ref options.addstyle)) { }
					else if (Options.TryParse(opt, "", ref options.addstyle)) { }
				}
			}

			return options;
		}

		internal static void SuppressCtrlAKeyDown(object o, KeyEventArgs e)
		{
#if WINDOWS
			if (e.KeyData == (Keys.Control | Keys.A))
				e.SuppressKeyPress = true;
#else
			if (e.KeyData == (Forms.Keys.Control | Forms.Keys.A))
				e.Handled = true;
#endif
		}

		internal static void SetContentAlignment(Forms.Control ctrl, GuiOptions opts)
		{
#if WINDOWS
			if (!opts.halign.HasValue && !opts.valign.HasValue)
				return;

			ContentAlignment current = Reflections.SafeGetProperty<ContentAlignment>(ctrl, "TextAlign");

			var horizontal = GetHorizontalAlignment(current);
			var vertical = GetVerticalAlignment(current);

			if (opts.halign.HasValue)
				horizontal = opts.halign.Value;

			if (opts.valign.HasValue)
				vertical = opts.valign.Value;

			Reflections.SafeSetProperty(ctrl, "TextAlign", CombineAlignment(vertical, horizontal));
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

			//Special style, windows only. Need to figure out how to make this cross platform.//TODO
			foreach (var split in Options.ParseOptions(options))
			{
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
			var dpiscale = !dpiscaling ? 1.0 : A_ScaledScreenDPI;
			var tabControls = controls.Values.OfType<Gui.Control>().Where(gc => gc.Ctrl is KeysharpTabControl).ToHashSet();

			foreach (var tc in tabControls)
				((KeysharpTabControl)tc.Ctrl).AdjustSize(dpiscale, tc.requestedSize);
		}

		public object this[object controlname]
		{
			get
			{
				var handle = controlname.ParseLong(false);

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

				return Errors.ErrorOccurred($"No controls matched the handle, name, text, ClassNN or NetClassNN {controlname}.");
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

	/// <summary>
	/// A special two component iterator for <see cref="Gui"/> which returns the key as a Control or
	/// the key and value as the Handle and Control as a tuple.
	/// </summary>
	internal class GuiControlIterator : MapKeyValueIterator
	{
		public GuiControlIterator(Dictionary<object, object> m, int c)
			: base(m, c)
		{
		}

		public override (object, object) Current
		{
			get
			{
				try
				{
					var kv = iter.Current;
					return Count == 1 ? (kv.Value, null) : (kv.Key, kv.Value);
				}
				catch (IndexOutOfRangeException)
				{
					throw new InvalidOperationException();
				}
			}
		}
	}
}
