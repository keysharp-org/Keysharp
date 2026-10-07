#if !WINDOWS
namespace Keysharp.Builtins
{
	public partial class Gui : KeysharpObject
	{
		public partial class Control : KeysharpObject
		{
			//The ListView selection and focus last reported to the script, which ItemSelect and ItemFocus are diffed against.
			//Held by row rather than by number, so that rows inserted or deleted before them do not change them.
			private HashSet<KeysharpListView.ListViewItem> listViewSelection;
			private KeysharpListView.ListViewItem listViewFocus;
			private bool listViewCheckClickActive;
			private bool listViewMouseSelectPending;
			public string ClassNN => _control is Window && WindowQuery.CreateWindow(_control.Handle) is WindowInfoBase wi ? wi.ClassNN : "";

			public object Gui => gui != null && gui.TryGetTarget(out var g) ? g : Errors.ErrorOccurred("GUI control's parent GUI is no longer available.");

			public long Hwnd => _control is Eto.Widget w ? w.Handle.ToInt64() : 0L;

			public string NetClassNN
			{
				get
				{
					if (Hwnd == 0)
						return "";
					return WindowQuery.CreateWindow((nint)Hwnd) is WindowInfoBase wi ? wi.NetClassNN : "";
				}
			}

			public object Parent
			{
				// A control's parent is either the container control holding it (a GroupBox or Tab page) or the
				// GUI's own form. Return the script object in both cases: handing back the toolkit control would
				// give a script an object with no script semantics, and it could not be assigned back here.
				get => ParentObject(_control?.Parent);
				set
				{
					if (_control == null)
						return;

					if (value is Gui.Control gc)
						AssignParent(gc._control);
					else if (value is Forms.Control c)
						AssignParent(c);
				}
			}

			public object Text
			{
				get
				{
					if (_control == null)
						return Errors.ErrorOccurred("GUI control is no longer available.");

					if (_control is KeysharpListBox lb)
					{
						var item = lb.SelectedIndex >= 0 ? lb.Items[lb.SelectedIndex]?.ToString() : null;

						if (lb.SelectionMode == SelectionMode.One)
							return item ?? "";

						return item != null ? Array.Literal(item) : new Array();
					}

					if (_control is KeysharpComboBox cb)
					{
						if (cb.DropDownStyle != ComboBoxStyle.DropDownList)
							return cb.Text ?? "";

						return cb.SelectedIndex >= 0 ? cb.Items[cb.SelectedIndex]?.ToString() ?? "" : "";
					}

					if (_control is KeysharpStatusStrip ss)
						return ss.Items.Count > 0 ? ss.Items[0].Text : "";

					if (_control is KeysharpTabControl tc && tc.SelectedPage != null)
						return tc.SelectedPage.Text ?? "";
					else if (_control is KeysharpGroupBox gb)
						return gb.Text;
					else
						return _control.Text ?? "";
				}
				set
				{
					if (_control == null)
						return;

					if (!value.CoerceString(out var s))
						return;

					if (this is List list && _control is KeysharpListBox or KeysharpComboBox)
						_ = list.ChooseItem(s, true);
					else if (_control is KeysharpTabControl tc)
						tc.SelectTab(s);
					else if (_control is KeysharpGroupBox gb)
						gb.Text = s;
					//A status bar's text is its first part's, as SetWindowText sets it in AutoHotkey.
					else if (this is StatusBar sb)
						_ = sb.SetText(s);
					else
						_control.Text = s;

					if (ParentForm?.Visible == true)
						_control.Invalidate();
				}
			}

			public object Value
			{
				get
				{
					if (_control is KeysharpNumericUpDown nud)
					{
						var v = (decimal)nud.Value;
						if (v == decimal.Truncate(v) && v >= long.MinValue && v <= long.MaxValue)
							return (long)v;
						return (double)v;
					}
					else if (_control is KeysharpCheckBox cb)
					{
						if (cb.CheckState == CheckState.Checked)
							return 1L;
						else if (cb.CheckState == CheckState.Unchecked)
							return 0L;
						else
							return -1L;
					}
					else if (_control is KeysharpRadioButton rb)
						return rb.Checked ? 1L : 0L;
					else if (_control is KeysharpComboBox cmb)
					{
						if (cmb.DropDownStyle == ComboBoxStyle.DropDown)
						{
							var indexof = cmb.Items.IndexOf(cmb.Text);

							if (indexof == -1)
								return 0L;
						}

						return (long)cmb.SelectedIndex + 1;
					}
					else if (_control is KeysharpListBox lb)
					{
						var position = lb.SelectedIndex + 1L;

						if (lb.SelectionMode == SelectionMode.One)
							return position;

						return position > 0 ? Array.Literal(position) : new Array();
					}
					else if (_control is KeysharpDateTimePicker dtp)
						return Conversions.ToYYYYMMDDHH24MISS(dtp.Value.GetValueOrDefault());
					else if (_control is KeysharpMonthCalendar mc)
						return $"{mc.SelectedDate:yyyyMMdd}";
					else if (_control is KeysharpTrackBar tb)
						return (long)tb.Value;
					else if (_control is KeysharpProgressBar pb)
						return (long)pb.Value;
					else if (_control is KeysharpTabControl tc)
						return (long)tc.SelectedIndex + 1;
					//A StatusBar has no value in AutoHotkey, only the text of its parts.
					else if (_control is KeysharpStatusStrip)
						return DefaultObject;
					else if (_control is KeysharpPictureBox pic)
						return pic.Filename;
					else if (_control is TextControl ctrl)
						return Conversions.ReplaceLineEndings(ctrl.Text);

					return DefaultObject;
				}
				set
				{
					if (!value.CoerceString(out var val))
						return;

					if (_control is KeysharpNumericUpDown nud)
						nud.Value = (double)value.ParseDouble().Value;
					else if (_control is KeysharpCheckBox cb)
					{
						if (!value.CoerceInt(out var ival))
							return;

						//Assigning Checked raises CheckedChanged, where the Click handlers hang. Only the user
						//toggling the box is a Click, so hold them off for this assignment.
						var wasActive = eventHandlerActive;
						eventHandlerActive = false;

						try
						{
							cb.CheckState = ival == -1 ? CheckState.Indeterminate
											: (Options.OnOff(value) ?? false) ? CheckState.Checked : CheckState.Unchecked;
						}
						finally
						{
							eventHandlerActive = wasActive;
						}
					}
					else if (_control is KeysharpRadioButton rb)
						rb.Checked = Options.OnOff(value) ?? false;
					else if (this is List list && _control is KeysharpListBox or KeysharpComboBox)
					{
						if (!value.CoerceLong(out var position))
							return;

						_ = list.ChooseItem(position, true);
					}
					else if (_control is KeysharpDateTimePicker dtp)
					{
						if (val?.Length == 0)
							return;
						else
							dtp.Value = Conversions.ToDateTime(val);
					}
					else if (_control is KeysharpMonthCalendar mc)
					{
						Conversions.ParseRange(val, out var dtlow, out var dthigh);

						if (dtlow == System.DateTime.MinValue)
							dtlow = dthigh;

						if (dthigh == System.DateTime.MaxValue)
							dthigh = dtlow;

						mc.SelectedDate = dtlow;
					}
					else if (_control is KeysharpTrackBar tb)
					{
						if (!value.CoerceInt(out var ival))
							return;

						tb.Value = ival;
					}
					else if (_control is KeysharpProgressBar pb)
					{
						if (!value.CoerceInt(out var ival))
							return;

						pb.Value = ival;
					}
					else if (_control is KeysharpTabControl tc)
					{
						if (!value.CoerceInt(out var ival))
							return;

						tc.SelectedIndex = ival - 1;
					}
					else if (_control is KeysharpStatusStrip)
						_ = Errors.ErrorOccurred("Invalid usage.");
					else if (_control is KeysharpPictureBox pic)
					{
						if (val == "")
						{
							var oldimage = pic.Image;
							pic.Image = null;

							if (oldimage is Bitmap oldbmp)
								oldbmp.Dispose();
						}
						else
						{
							var width = int.MinValue;
							var height = int.MinValue;
							var icon = "";
							object iconnumber = 0L;
							var filename = "";

							foreach (Range r in val.AsSpan().SplitAny(SpaceTabSv))
							{
								var opt = val.AsSpan(r).Trim();

								if (opt.Length > 0)
								{
									if (Options.TryParse(opt, "*w", ref width)) { }
									else if (Options.TryParse(opt, "*h", ref height)) { }
									else if (Options.TryParseString(opt, "*icon", ref icon)) { iconnumber = ImageHelper.PrepareIconNumber(icon); }
									else
									{
										filename = val.Substring(r.Start.Value);
										break;
									}
								}
							}

							// If the value of a PictureBox is changed then the size of the box
							// should remain the same and the picture should be centered in it.
							// Otherwise the picture sizing logic is mostly the same as when initializing
							// a PictureBox: negative width/height means fit to the size of the box,
							// w0/h0 means use original size, positive size uses the custom size.

							if (pic.SizeMode != PictureBoxSizeMode.CenterImage)
								pic.SizeMode = PictureBoxSizeMode.CenterImage;

							// Before layout, GTK's allocation is 1x1; use the assigned control size.
							var size = pic.GetSize();
							if (width == int.MinValue)
								width = size.Width;
							if (height == int.MinValue)
								height = size.Height;

							var (bmp, source) = ImageHelper.LoadImage(filename, width, height, iconnumber);
							(source as IDisposable)?.Dispose();

							if (bmp == null)
							{
								_ = Errors.ValueErrorOccurred("Invalid value.", filename);
								return;
							}

							var oldimage = pic.Image;
							pic.Image = bmp;

							if (oldimage is Bitmap oldbmp)
								oldbmp.Dispose();
						}
					}
					else if (_control is TextControl ctrl)
						ctrl.Text = Conversions.ReplaceLineEndings(val, Environment.NewLine);
					else if (_control != null)
						_control.Text = val;

					if (ParentForm?.Visible == true)
						_control.Invalidate();
				}
			}

			public Control(params object[] args) : base(args)
			{
				//Refused rather than half-built, and checked by type rather than just by count: a Control with
				//no toolkit control behind it has no working member at all, and reaching through the null raises
				//a NullReferenceException that escapes the script's try/catch and kills the thread.
				if (args == null || args.Length < 3 || args[0] is not Keysharp.Builtins.Gui || args[1] is not Forms.Control || args[2] == null)
					throw new Error("Gui.Control cannot be constructed directly; add controls with Gui.Add().");

				var g = args[0] as Gui;
				var control = args[1] as Forms.Control;
				var name = args[2].ToString();
				var wrap = args.Length > 3 ? args[3].Ab() : false;
				gui = new WeakReference<Gui>(g);
				typename = name;
				_control = control;
				_control.Tag = new GuiTag()
				{
					GuiControl = this,
					Index = _control.Parent != null ? _control.Parent.Controls.Count() : 0
				};

				if (wrap)//Just a holder for the controls in the main window.
					return;

				if (_control is Forms.Button btn)
					btn.Click += _control_Click;
				else if (_control is KeysharpRadioButton rb)
					rb.Click += _control_Click;
				else if (_control is KeysharpCheckBox chk)
					//NOT the MouseDown fallback below: that runs before the box has toggled, so a handler
					//reading Value would see the state the click is about to replace, and a keyboard toggle
					//would not report at all. Matches WinForms, whose Click fires after the state changes.
					chk.CheckedChanged += _control_Click;
				else if (_control is LinkButton link)
					link.Click += _control_Click;
				else if (_control is ICommandItem ti)
					ti.Click += _control_Click;
				else if (_control is TabControl tc)
				{
					foreach (var tp in tc.Pages)
						tp.Click += _control_Click;
				}
				else
					_control.MouseDown += _control_Click;

				// Do not hook TabControl mouse-down to the generic click handler; on GTK/Eto it swallows
				// child clicks (e.g. DateTimePicker drop-downs) when the control sits inside a tab.
				if (_control is not KeysharpTabControl)
				{
					_control.MouseDown += _control_MouseDown;
					_control.MouseDoubleClick += _control_DoubleClick;
				}

				if (_control is KeysharpTreeView tv)
				{
					tv.SelectedItemChanged += Tv_AfterSelect;
					tv.Expanded += Tv_AfterExpand;
					tv.CellEdited += Tv_AfterLabelEdit;
					tv.CellEdited += Tv_AfterCheck;
					tv.CellClick += Tv_CellClick;
				}
				else if (_control is KeysharpListView lv)
				{
					lv.CellClick += Lv_CellClick;
					lv.SelectedRowsChanged += Lv_SelectedRowsChanged;
					lv.ColumnClicked += Lv_ColumnClick;
					lv.CellEdited += Lv_AfterLabelEdit;
					lv.MouseDoubleClick += Lv_MouseDoubleClickEdit;
				}
				else if (_control is KeysharpTrackBar tb)
				{
					//tb.MouseCaptureChanged += Tb_MouseCaptureChanged;
					tb.ValueChanged += Tb_ValueChanged;
				}
				else if (_control is KeysharpTabControl tc)
				{
					tc.SelectedIndexChanged += Tc_Selected;
				}
				else if (_control is KeysharpNumericUpDown nud)
				{
					nud.ValueChanged += Nud_ValueChanged;
				}
				else if (_control is HotkeyBox hkb)
				{
					hkb.TextChanged += Hkb_TextChanged;
				}
				else if (_control is KeysharpMonthCalendar mc)
				{
					mc.SelectedDateChanged += Mc_DateChanged;
				}
				else if (_control is KeysharpDateTimePicker dtp)
				{
					dtp.ValueChanged += Dtp_ValueChanged;
				}
				else if (_control is TextControl txt && _control is not KeysharpRadioButton && _control is not KeysharpCheckBox)
				{
					//CheckBox/RadioButton derive from Eto's TextControl but their text is fixed, so TextChanged is
					//meaningless for them - and some backends (e.g. Eto.Mac's CheckBoxHandler) don't implement it,
					//which logs a "not supported" warning. Their state changes are delivered via the Click event.
					txt.TextChanged += Txt_TextChanged;
				}
				else if (_control is KeysharpListBox lb)
				{
					lb.SelectedIndexChanged += Lb_SelectedIndexChanged;
				}
				else if (_control is KeysharpComboBox cmb)
				{
					cmb.SelectedIndexChanged += Cmb_SelectedIndexChanged;
				}

				_control.GotFocus += _control_GotFocus;
				_control.LostFocus += _control_LostFocus;
				_control.KeyDown += _control_KeyDown;
			}

			public object Move(object x = null, object y = null, object width = null, object height = null)
			{
				if (_control == null)
					return Errors.ErrorOccurred("GUI control is no longer available.");

				var location = _control.GetLocation();
				var size = _control.GetSize();

				if (!x.CoerceInt(out var nx, location.X)
						|| !y.CoerceInt(out var ny, location.Y)
						|| !width.CoerceInt(out var nw, size.Width)
						|| !height.CoerceInt(out var nh, size.Height))
					return DefaultObject;

				_control.SetLocation(new Point(nx, ny));
				_control.SetSize(new Size(nw, nh));
				return DefaultObject;
			}

			//WM_COMMAND and WM_NOTIFY are Win32 concepts with no Eto/GTK/Cocoa equivalent, so this is accepted
			//(so a cross-platform script still loads) but never fires.
			public object OnCommand(object notifyCode, object callback, object addRemove = null) => CheckUnfired(callback, addRemove, 1);

			//Boxed, because the shared GuiHelper.CallMessageHandler consumes the Windows overload's object
			//return, and that one carries the WM_COMMAND/WM_NOTIFY reflection this platform has no use for.
			internal object InvokeMessageHandlers(ref Message m) => InvokeWindowMessageHandlers(ref m);

			public object OnNotify(object notifyCode, object callback, object addRemove = null) => CheckUnfired(callback, addRemove, 2);

			//Checked as Windows checks it, so a registration refused there is refused here too.
			private object CheckUnfired(object callback, object addRemove, int argCount)
			{
				if (gui == null || !gui.TryGetTarget(out var g))
					return Errors.ErrorOccurred("GUI control's parent GUI is no longer available.");

				_ = KeysharpForm.CheckedHandler(callback, g.form, addRemove, argCount, out _);
				return DefaultObject;
			}
			public object Opt(object options)
			{
				if (gui == null || !gui.TryGetTarget(out var g))
					return Errors.ErrorOccurred("GUI control's parent GUI is no longer available.");

				if (!options.CoerceString(out var optionText))
					return DefaultObject;

				var opts = g.ParseOpt(typename, _control.Text, optionText);

				if (opts.dpiresize.HasValue)
					dpiResize = opts.dpiresize.Value;

				if (opts.redraw.HasValue)
				{
					if (opts.redraw == false)
					{
						_control.SuspendDrawing();
					}
					else
					{
						//if (_control is KeysharpListView klv)
						//	klv.SetListViewColumnSizes();

						_control.ResumeDrawing();
					}
				}

				if (opts.c.HasValue)
				{
					if (_control is KeysharpProgressBar pb)
						pb.BarColor = opts.c.Value;
					else
						_control.ForeColor = opts.c.Value;
				}

				if (_control is KeysharpButton)
				{
					//if (opts.btndef.HasValue)
					//	g.form.AcceptButton = opts.btndef == true ? (IButtonControl)_control : null;
				}
				else if (_control is KeysharpListBox lb)
				{
					if (opts.sort.HasValue)
						lb.Sorted = opts.sort.Value;
				}
				else if (_control is KeysharpComboBox cb)
				{
					if (opts.sort.HasValue)
					{
						cb.Sorted = opts.sort.Value;

						if (cb.DropDownStyle != ComboBoxStyle.DropDownList)
							cb.AutoComplete = opts.sort.Value;
					}

					if (typename != Keyword_DropDownList && opts.cmbsimple.HasValue)
						cb.DropDownStyle = opts.cmbsimple.IsTrue() ? ComboBoxStyle.Simple : ComboBoxStyle.DropDown;
				}
				else if (_control is KeysharpTextBox txt)
				{
					if (opts.rdonly.HasValue)
						txt.ReadOnly = opts.rdonly.Value;

					SetWantCtrlA(txt, opts.wantctrla);

					if (opts.limit != int.MinValue)
						txt.MaxLength = opts.limit;

					if (opts.number.HasValue)
						txt.IsNumeric = opts.number.Value;

					txt.CharacterCasing = opts.Casing(txt.CharacterCasing);
				}
				else if (_control is KeysharpPasswordBox ptxt)
				{
					if (opts.rdonly.HasValue)
						ptxt.ReadOnly = opts.rdonly.Value;

					SetWantCtrlA(ptxt, opts.wantctrla);

					if (opts.limit != int.MinValue)
						ptxt.MaxLength = opts.limit;

					if (opts.number.HasValue)
						ptxt.IsNumeric = opts.number.Value;

					ptxt.CharacterCasing = opts.Casing(ptxt.CharacterCasing);

					if (opts.pwd && opts.pwdch != "")
						ptxt.PasswordChar = opts.pwdch[0];
				}
				else if (_control is KeysharpTextArea ttxt)
				{
					if (opts.wanttab.HasValue)
						ttxt.AcceptsTab = opts.wanttab.Value;

					if (opts.wantreturn.HasValue)
						ttxt.AcceptsReturn = opts.wantreturn.Value;

					if (opts.rdonly.HasValue)
						ttxt.ReadOnly = opts.rdonly.Value;

					SetWantCtrlA(ttxt, opts.wantctrla);

					if (opts.number.HasValue)
						ttxt.IsNumeric = opts.number.Value;

					ttxt.CharacterCasing = opts.Casing(ttxt.CharacterCasing);

					if (opts.wordwrap.HasValue)
						ttxt.Wrap = opts.wordwrap.IsTrue();
				}
				else if (_control is KeysharpRichEdit rtxt)
				{
					if (opts.wanttab.HasValue)
						rtxt.AcceptsTab = opts.wanttab.Value;

					if (opts.rdonly.HasValue)
						rtxt.ReadOnly = opts.rdonly.Value;

					SetWantCtrlA(rtxt, opts.wantctrla);

					if (opts.number.HasValue)
						rtxt.IsNumeric = opts.number.Value;

					rtxt.CharacterCasing = opts.Casing(rtxt.CharacterCasing);
				}
				else if (_control is KeysharpTrackBar tb)
				{
					if (opts.halign.HasValue)
					{
						if (opts.halign.Value == GuiOptions.HorizontalAlignment.Center)
							tb.TickStyle = TickStyle.Both;
						else if (opts.halign.Value == GuiOptions.HorizontalAlignment.Left)
						tb.TickStyle = TickStyle.TopLeft;
					}
					if (opts.noticks.IsTrue())
						tb.TickStyle = TickStyle.None;

					if (opts.invert.HasValue)
						tb.inverted = opts.invert.Value;

					if (opts.tickinterval != int.MinValue)
						tb.TickFrequency = opts.tickinterval;

					if (opts.line != int.MinValue)
						tb.SmallChange = opts.line;

					if (opts.page != int.MinValue)
						tb.LargeChange = opts.page;
				}
				else if (_control is KeysharpTreeView tv)
				{
					if (opts.rdonly.HasValue)
						tv.LabelEdit = !opts.rdonly.Value;

					if (tv.LabelEdit)
					{
						if (opts.wantf2.HasValue && opts.wantf2.IsFalse())
							tv.KeyDown -= Builtins.Gui.Tv_Lv_KeyDown;
						else
						{
							tv.KeyDown -= Builtins.Gui.Tv_Lv_KeyDown;
							tv.KeyDown += Builtins.Gui.Tv_Lv_KeyDown;
						}
					}
					else
					{
						tv.KeyDown -= Builtins.Gui.Tv_Lv_KeyDown;
					}
				}
				else if (_control is KeysharpListView lv)
				{
					if (opts.ischecked.HasValue)
						lv.CheckBoxes = opts.ischecked.Value > 0;

					if (opts.rdonly.HasValue)
						lv.LabelEdit = !opts.rdonly.Value;

					if (opts.grid.HasValue)
						lv.GridLines = opts.grid.IsTrue();

					if (opts.multiline.HasValue)
						lv.MultiSelect = opts.multiline.Value;

					if (lv.LabelEdit)
					{
						if (opts.wantf2.HasValue && opts.wantf2.IsFalse())
							lv.KeyDown -= Builtins.Gui.Tv_Lv_KeyDown;
						else
						{
							lv.KeyDown -= Builtins.Gui.Tv_Lv_KeyDown;
							lv.KeyDown += Builtins.Gui.Tv_Lv_KeyDown;
						}
					}
					else
					{
						lv.KeyDown -= Builtins.Gui.Tv_Lv_KeyDown;
					}

					if (opts.lvview.HasValue)
						lv.View = opts.lvview.Value;

					if ((opts.addlvstyle & 0x10) == 0x10)
						lv.AllowColumnReorder = true;
					else if ((opts.remlvstyle & 0x10) == 0x10)
						lv.AllowColumnReorder = false;

					if (opts.sort.IsTrue())
						lv.Sorting = SortOrder.Ascending;
					else if (opts.sortdesc.IsTrue())
						lv.Sorting = SortOrder.Descending;
					else if (opts.sort.IsFalse() || opts.sortdesc.IsFalse())//If either were reset, just set to none.
						lv.Sorting = SortOrder.None;

					if (opts.header.HasValue)
						lv.HeaderStyle = opts.header.IsFalse() ? ColumnHeaderStyle.None : ColumnHeaderStyle.Clickable;
					else if (opts.clickheader.HasValue)
						lv.HeaderStyle = opts.clickheader.IsFalse() ? ColumnHeaderStyle.Nonclickable : ColumnHeaderStyle.Clickable;

					if (opts.sortheader.HasValue)
						lv.AutoSortHeader = opts.sortheader.IsTrue();
					else if (opts.clickheader.HasValue && opts.clickheader.IsFalse())
						lv.AutoSortHeader = false;
					else
						lv.AutoSortHeader = true;
				}
				else if (_control is KeysharpProgressBar pb)
				{
					if (opts.smooth.HasValue)
						pb.Style = opts.smooth.IsTrue() ? ProgressBarStyle.Continuous : ProgressBarStyle.Blocks;
				}
				else if (_control is KeysharpTabControl tc)
				{
					if (opts.buttons.HasValue)
						tc.Appearance = opts.buttons.Value ? TabAppearance.FlatButtons : TabAppearance.Normal;

					if (opts.wordwrap.HasValue)
						tc.Multiline = opts.wordwrap.IsTrue();

					if (opts.halign.HasValue)
					{
						if (opts.halign.Value == GuiOptions.HorizontalAlignment.Left)
							tc.Alignment = TabAlignment.Left;
						else if (opts.halign.Value == GuiOptions.HorizontalAlignment.Right)
							tc.Alignment = TabAlignment.Right;
					}
					if (opts.valign.HasValue)
					{
						if (opts.valign.Value == GuiOptions.VerticalAlignment.Bottom)
							tc.Alignment = TabAlignment.Bottom;
						else if (opts.valign.Value == GuiOptions.VerticalAlignment.Top)
							tc.Alignment = TabAlignment.Top;
					}

					if (opts.bgtrans)
						tc.SetColor(Color.Transparent);
					else if (opts.bgcolor.HasValue)
						tc.SetColor(opts.bgcolor.Value);
				}
				else if (_control is KeysharpNumericUpDown nud)
				{
					if (opts.nudinc.HasValue)
						nud.Increment = opts.nudinc.Value;

					if (opts.nudlow.HasValue)
						nud.Minimum = opts.nudlow.Value;

					if (opts.nudhigh.HasValue)
						nud.Maximum = opts.nudhigh.Value;
				}

				SetContentAlignment(_control, opts);

				if (opts.bgtrans)
					_control.BackColor = Color.Transparent;
				else if (opts.bgcolor.HasValue)
					_control.BackColor = opts.bgcolor.Value;

				if (opts.altsubmit.HasValue)
					AltSubmit = opts.altsubmit.Value;

				if (opts.visible.HasValue)
					_control.Visible = opts.visible.Value;

				if (opts.enabled.HasValue)
					_control.Enabled = opts.enabled.Value;

				//if (opts.tabstop.HasValue)
				//	_control.TabStop = opts.tabstop.Value;

				if (opts.wordwrap.HasValue)
					Reflections.SafeSetProperty(_control, "WordWrap", opts.wordwrap.Value);

				//if (opts.thinborder.HasValue)
				//	Reflections.SafeSetProperty(_control, "BorderStyle", opts.thinborder.Value ? BorderStyle.FixedSingle : BorderStyle.None);

				return DefaultObject;
			}

			private static void SetWantCtrlA(Forms.Control edit, bool? want)
			{
				if (!want.HasValue)
					return;

				edit.KeyDown -= Builtins.Gui.SuppressCtrlAKeyDown;

				if (!want.Value)
					edit.KeyDown += Builtins.Gui.SuppressCtrlAKeyDown;
			}

			public object Redraw()
			{
				_control.Invalidate();
				return DefaultObject;
			}

			private void AssignParent(Forms.Control parent)
			{
				if (parent is not Forms.PixelLayout container)
					return;

				if (_control.Parent is Forms.Container oldContainer)
					oldContainer.Remove(_control);

				container.Add(_control, _control.Location);
			}

			private Point ConvertToClientPoint(int x, int y)
			{
				if (_control.ParentWindow is Window win)
				{
					if (win is KeysharpForm form)
						return form.PointToGuiClient(new PointF(x, y));

					var client = win.PointFromScreen(new PointF(x, y));
					return new Point(Convert.ToInt32(client.X), Convert.ToInt32(client.Y));
				}

				return new Point(x, y);
			}

			internal void _control_Click(object sender, EventArgs e)
			{
				//Where this runs on MouseDown, only the left button is a click, as in AutoHotkey.
				if (!eventHandlerActive || e is MouseEventArgs { Buttons: not MouseButtons.Primary })
					return;

				if (_control is KeysharpListView lv)
				{
					//A click on a row is reported from Lv_CellClick, which is given the row; MouseDown comes before the
					//selection follows it. A click on no row raises no CellClick, and AutoHotkey reports it as row 0.
					if (lv.GetCellAt((e as MouseEventArgs)?.Location ?? default) is null or { Type: GridCellType.None })
						clickHandlers?.InvokeEventHandlers(this, 0L);
				}
				else if (_control is KeysharpTreeView)
				{
					//As for a ListView row, the clicked node is reported from Tv_CellClick.
				}
				else if (_control is KeysharpLinkLabel ll)
				{
					var idx = ll.LinkIndexAt((e as MouseEventArgs)?.Location);

					if (idx >= 0)
					{
						var id = ll.links[idx].Item3.Item1;
						var url = ll.links[idx].Item3.Item2;

						if (!ll.clickSet)
							KeysharpLinkLabel.OpenUrl(url);
						else
							clickHandlers.InvokeEventHandlers(this, id != "" ? id : idx + 1L, url);
					}
				}
				else if (_control is KeysharpStatusStrip sbar)
					clickHandlers.InvokeEventHandlers(this, sbar.PartFromPoint());
				//else if (_control is KeysharpButton)
				//{
				//  //mousecount ^= 1;//Button click events get fired twice, because we have double click and standard click enabled, so filter the second click here.
				//  //if (mousecount > 0)
				//  _ = clickHandlers.InvokeEventHandlers(this, 0L);
				//}
				else
					clickHandlers.InvokeEventHandlers(this, 0L);
			}

			internal void _control_KeyDown(object sender, KeyEventArgs e)
			{
#if !OSX
				// The Menu/context-menu key and Shift+F10 open the context menu on Windows and Linux.
				// macOS has no such key (and uses Ctrl+click, handled in _control_MouseDown), so it is omitted there.
				if (e.Key == Forms.Keys.ContextMenu || (e.Key == Forms.Keys.F10 && ((e.Modifiers & Forms.Keys.Shift) == Forms.Keys.Shift)))
					RaiseContextMenu(true, default);
#endif
			}

			internal void _control_MouseDown(object sender, MouseEventArgs e)
			{
				if (e.Buttons == MouseButtons.Alternate)
					RaiseContextMenu(false, e.Location);
			}

			/// <summary>Raises one ContextMenu event through this control's window.</summary>
			internal void RaiseContextMenu(bool fromKeyboard, PointF location)
			{
				if (!eventHandlerActive || gui == null || !gui.TryGetTarget(out var g) || !g.form.TryBeginContextMenu())
					return;

				var item = _control switch
				{
					KeysharpListBox lb => lb.SelectedIndex + 1L,
					KeysharpListView lv when !fromKeyboard => lv.GetCellAt(location) is { RowIndex: >= 0 } cell ? cell.RowIndex + 1L : 0L,
					KeysharpListView lv => lv.FocusedRow + 1L,
					KeysharpTreeView tv when !fromKeyboard => tv.NodeAt(location)?.Handle.ToInt64() ?? 0L,
					KeysharpTreeView tv => tv.SelectedNode?.Handle.ToInt64() ?? 0L,
					KeysharpStatusStrip sbar when !fromKeyboard => sbar.PartFromPoint(),
					_ => 0L
				};
				var screen = _control.PointToScreen(fromKeyboard ? new PointF(0, 2 + _control.GetSize().Height / 2) : location);
				var client = ConvertToClientPoint(Convert.ToInt32(screen.X), Convert.ToInt32(screen.Y));
				g.form.RaiseContextMenu(this, item, !fromKeyboard, client.X, client.Y);
			}

			internal void Tv_AfterExpand(object sender, TreeGridViewItemEventArgs e)
			{
				if (eventHandlerActive && _control is KeysharpTreeView)
					itemExpandHandlers?.InvokeEventHandlers(this, (e.Item as TreeNode)?.Handle.ToInt64() ?? 0L, e.Item.Expanded ? 1L : 0L);
			}

			internal void Lv_AfterLabelEdit(object sender, GridViewCellEventArgs e)
			{
				if (!eventHandlerActive || _control is not KeysharpListView lv)
					return;

				if (e.Item is not KeysharpListView.ListViewItem item)
					return;

				var rowIndex = lv.Items.IndexOf(item);
				if (rowIndex < 0)
					return;

				if (lv.HasCheckBoxes && e.Column == ((Eto.Forms.GridView)lv).Columns.IndexOf(lv.CheckColumn))
				{
					if (listViewCheckClickActive)
					{
						listViewCheckClickActive = false;
						return;
					}
					itemCheckHandlers?.InvokeEventHandlers(this, rowIndex + 1L, item.Checked ? 1L : 0L);
					return;
				}

				itemEditHandlers?.InvokeEventHandlers(this, rowIndex + 1L);
			}

			internal void Tv_AfterLabelEdit(object sender, GridViewCellEventArgs e)
			{
				if (!eventHandlerActive || _control is not KeysharpTreeView)
					return;

				if (e.Item is TreeNode node)
					itemEditHandlers?.InvokeEventHandlers(this, node.Handle.ToInt64());
			}

			internal void Tv_AfterCheck(object sender, GridViewCellEventArgs e)
			{
				if (!eventHandlerActive || _control is not KeysharpTreeView tv || !tv.HasCheckBoxes)
					return;

				if (e.Column == tv.Columns.IndexOf(tv.CheckColumn) && e.Item is TreeNode node)
					itemCheckHandlers?.InvokeEventHandlers(this, node.Handle.ToInt64(), node.Checked ? 1L : 0L);
			}

			internal void Tv_CellClick(object sender, GridCellMouseEventArgs e)
			{
				if (!eventHandlerActive || _control is not KeysharpTreeView tv || e.Item is not TreeNode node)
					return;

				//Clicking the checkbox column toggles the check and raises ItemCheck (not Click).
				if (tv.HasCheckBoxes && e.Column == tv.Columns.IndexOf(tv.CheckColumn) && e.Buttons == MouseButtons.Primary)
				{
					node.Checked = !node.Checked;
					tv.CheckedBeginInvoke(new Action(() => tv.ReloadItem(node, false)), true, false);
					itemCheckHandlers?.InvokeEventHandlers(this, node.Handle.ToInt64(), node.Checked ? 1L : 0L);
					return;
				}

				//This is the AHK "Click" event: report the node that was actually clicked (e.Item),
				//independent of the (not-yet-updated) selection, so a single click works.
				if (e.Buttons == MouseButtons.Primary)
				{
					//Commit the clicked node as the selection before raising Click so the selection visual
					//and ItemSelect happen before Click (matching AutoHotkey, and the Windows backend). This
					//is a no-op when the click already updated the selection.
					if (tv.SelectedItem != node)
						tv.SelectedItem = node;

					clickHandlers?.InvokeEventHandlers(this, node.Handle.ToInt64());
				}
			}

			internal void Lv_CellClick(object sender, GridCellMouseEventArgs e)
			{
				if (!eventHandlerActive || _control is not KeysharpListView lv)
					return;

				if (e.Row < 0)
				{
					lv.HandleHeaderClick(e.Column);
					return;
				}

				if (e.Item is not KeysharpListView.ListViewItem item)
					return;

				var rowIndex = e.Row;

				var grid = (Eto.Forms.GridView)lv;
				var checkColumnIndex = lv.HasCheckBoxes ? grid.Columns.IndexOf(lv.CheckColumn) : -1;
				var checkClick = checkColumnIndex >= 0 && e.Column == checkColumnIndex && e.Buttons == MouseButtons.Primary;
				if (checkClick)
				{
					listViewCheckClickActive = true;
					item.Checked = !item.Checked;
					lv.ReloadData(rowIndex);
					itemCheckHandlers?.InvokeEventHandlers(this, rowIndex + 1L, item.Checked ? 1L : 0L);
					_ = Eto.Forms.Application.Instance.InvokeAsync(() => listViewCheckClickActive = false);

				}

				lv.FocusedItem = item;
				//The toolkit applies Ctrl/Shift selection after CellClick; selecting here changes its anchor or toggle.
				if (lv.MultiSelect && (e.Modifiers & (Forms.Keys.Control | Forms.Keys.Shift | Forms.Keys.Application)) != 0)
				{
					listViewMouseSelectPending = true;
					lv.CheckedBeginInvoke(() =>
					{
						if (_control != lv || lv.IsDisposed)
							return;

						Lv_SelectedRowsChanged(sender, EventArgs.Empty);
						listViewMouseSelectPending = false;

						if (!checkClick && e.Buttons == MouseButtons.Primary)
							clickHandlers?.InvokeEventHandlers(this, rowIndex + 1L);
					}, false, true);
					return;
				}

				if (lv.MultiSelect && e.Buttons != MouseButtons.Primary)
					grid.SelectRow(rowIndex);
				else
					grid.SelectedRow = rowIndex;

				Lv_SelectedRowsChanged(sender, EventArgs.Empty);

				if (!checkClick && e.Buttons == MouseButtons.Primary)
					clickHandlers?.InvokeEventHandlers(this, rowIndex + 1L);
			}

			internal void Lv_SelectedRowsChanged(object sender, EventArgs e)
			{
				if (_control is not KeysharpListView lv)
					return;

				var grid = (Eto.Forms.GridView)lv;
				var items = lv.Items;
				var current = new HashSet<KeysharpListView.ListViewItem>();
				var first = -1;

				foreach (var row in grid.SelectedRows)
				{
					_ = current.Add(items[row]);

					if (first < 0 || row < first)
						first = row;
				}

				var previous = listViewSelection;
				var previousFocus = listViewFocus;
				listViewSelection = current;

				//A change the script made raises no event, as in AutoHotkey, and is not reported later either; the focus it
				//set stays as it set it.
				if (!eventHandlerActive)
				{
					listViewFocus = lv.FocusedItem;
					return;
				}

				//The user moved the selection away from the focused row, so the focus moves with it.
				if (!listViewMouseSelectPending && first >= 0 && (lv.FocusedItem is not { } focused || !current.Contains(focused)))
					lv.FocusedItem = items[first];

				listViewFocus = lv.FocusedItem;

				if (selectedItemChangedHandlers != null)
				{
					//Deselections first, as the native control reports them. A row the selection lost by being deleted is
					//no longer there to report.
					if (previous != null && !previous.IsSubsetOf(current))
					{
						for (var i = 0; i < items.Count; i++)
							if (previous.Contains(items[i]) && !current.Contains(items[i]))
								selectedItemChangedHandlers.InvokeEventHandlers(this, i + 1L, 0L);
					}

					foreach (var row in grid.SelectedRows)
						if (previous == null || !previous.Contains(items[row]))
							selectedItemChangedHandlers.InvokeEventHandlers(this, row + 1L, 1L);
				}

				if (focusedItemChangedHandlers != null && listViewFocus != null && listViewFocus != previousFocus)
					focusedItemChangedHandlers.InvokeEventHandlers(this, lv.FocusedRow + 1L);
			}

			internal void Lv_ColumnClick(int columnIndex)
			{
				if (eventHandlerActive && _control is KeysharpListView)
					columnClickHandlers?.InvokeEventHandlers(this, columnIndex + 1L);
			}

			internal void Lv_MouseDoubleClickEdit(object sender, MouseEventArgs e)
			{
				if (_control is KeysharpListView { LabelEdit: true } lv)
				{
					var row = ((Eto.Forms.GridView)lv).SelectedRow;
					lv.BeginEditRow(row >= 0 ? row : lv.FocusedRow);
				}
			}

			internal void Tv_AfterSelect(object sender, EventArgs e)
			{
				if (eventHandlerActive && _control is KeysharpTreeView tv)
					selectedItemChangedHandlers?.InvokeEventHandlers(this, (tv.SelectedItem as TreeNode)?.Handle.ToInt64() ?? 0L);
			}

			internal void Txt_TextChanged(object sender, EventArgs e)
			{
				//A RichEdit reports a formatting change as a text change, which is not one a script asked about.
				if (_control is KeysharpRichEdit { IsFormatting: true })
					return;

				if (eventHandlerActive && (_control is KeysharpTextBox || _control is KeysharpPasswordBox
										   || _control is KeysharpTextArea || _control is KeysharpRichEdit))
					changeHandlers?.InvokeEventHandlers(this, 0L);
			}
		}
	}
}
#endif
