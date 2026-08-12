using Keysharp.Runtime;
using CallbackHub = Keysharp.Internals.Scripting.CallbackRegistry<Keysharp.Internals.Scripting.CallbackRegistration>;

namespace Keysharp.Builtins
{
    internal class GuiTag
	{
		internal Gui.Control GuiControl { get; set; }
		internal int Index { get; set; }
	}

	public partial class Gui : KeysharpObject, I__Enum, IEnumerable<(object, object)>
	{
		public partial class Control : KeysharpObject
		{
			private string typename;
			private WeakReference<Gui> gui;
			private readonly CallbackHub clickHandlers = new();
			private readonly CallbackHub doubleClickHandlers = new();
			internal bool DpiScaling => ((Gui)Gui).DpiScale != 1.0;
			private Forms.Control _control;

			// Normal event handlers can't be used because they need to return a value.
			// The returned values are then inspected to determine whether subsequent handlers should be called.
			private CallbackHub changeHandlers;
			private CallbackHub columnClickHandlers;
#if WINDOWS
			private ConcurrentDictionary<int, CallbackHub> commandHandlers;
#endif
			private CallbackHub contextMenuChangedHandlers;
#if WINDOWS
			private nint dummyHandle;
#endif
			private CallbackHub focusedItemChangedHandlers;
			private CallbackHub focusHandlers;
			private CallbackHub itemCheckHandlers;
			private CallbackHub itemEditHandlers;
			private CallbackHub itemExpandHandlers;
			private CallbackHub lostFocusHandlers;
#if WINDOWS
			//Keyed by window message number, for OnMessage(). Separate from commandHandlers/notifyHandlers
			//because those key on a WM_COMMAND notification code / WM_NOTIFY code, not on the message itself.
			private ConcurrentDictionary<int, CallbackHub> messageHandlers;
			private ConcurrentDictionary<int, CallbackHub> notifyHandlers;
#endif
			private long parenthandle;
			private CallbackHub selectedItemChangedHandlers;
			internal Size requestedSize = new (int.MinValue, int.MinValue);
			internal bool eventHandlerActive = true;
			//"+/-DPIResize": whether this control is re-laid out when the GUI's DPI changes. Seeded from the
			//GUI's default when the control is added, so a later change to that default leaves this one alone.
			internal bool dpiResize = true;

			internal bool RemoveOwnedHandlers(ScriptEventScheduler scheduler)
			{
				var removedAny = clickHandlers.RemoveOwned(scheduler);
				removedAny |= doubleClickHandlers.RemoveOwned(scheduler);
				removedAny |= changeHandlers?.RemoveOwned(scheduler) == true;
				removedAny |= columnClickHandlers?.RemoveOwned(scheduler) == true;
#if WINDOWS
				removedAny |= CallbackRegistry<CallbackRegistration>.RemoveOwned(commandHandlers, scheduler);
#endif
				removedAny |= contextMenuChangedHandlers?.RemoveOwned(scheduler) == true;
				removedAny |= focusedItemChangedHandlers?.RemoveOwned(scheduler) == true;
				removedAny |= focusHandlers?.RemoveOwned(scheduler) == true;
				removedAny |= itemCheckHandlers?.RemoveOwned(scheduler) == true;
				removedAny |= itemEditHandlers?.RemoveOwned(scheduler) == true;
				removedAny |= itemExpandHandlers?.RemoveOwned(scheduler) == true;
				removedAny |= lostFocusHandlers?.RemoveOwned(scheduler) == true;
#if WINDOWS
				removedAny |= CallbackRegistry<CallbackRegistration>.RemoveOwned(messageHandlers, scheduler);
				removedAny |= CallbackRegistry<CallbackRegistration>.RemoveOwned(notifyHandlers, scheduler);
#endif
				removedAny |= selectedItemChangedHandlers?.RemoveOwned(scheduler) == true;

#if WINDOWS
				if (_control is StatusStrip ss)
				{
					foreach (var item in ss.Items)
					{
						if (item is KeysharpToolStripStatusLabel tssl)
							removedAny |= tssl.doubleClickHandlers.RemoveOwned(scheduler);
					}
				}
#elif !WINDOWS
				if (_control is KeysharpStatusStrip ss)
				{
					foreach (var item in ss.Items)
						removedAny |= item?.doubleClickHandlers.RemoveOwned(scheduler) == true;
				}
#endif

				return removedAny;
			}

			public bool AltSubmit { get; internal set; } = false;
			internal Forms.Control Ctrl => _control;

			public object Enabled
			{
				get => _control.Enabled;
				set => _control.Enabled = Options.OnOff(value) ?? false;
			}

			public object Focused => _control.Focused;


			public object Name
			{
				get => _control.Name;
				set => _control.Name = value.ToString();
			}

			internal KeysharpForm ParentForm => _control.FindParent<KeysharpForm>();
			public object RichText
			{
				get
				{
					if (_control is KeysharpRichEdit rtf)
						return Conversions.NormalizeEol(rtf.Text);

					return Errors.ErrorOccurred($"Can only get RichText on a RichEdit control. Attempted on a {_control?.GetType().Name ?? "null"} control.");
				}
				set
				{
					if (_control is KeysharpRichEdit rtf)
						rtf.Rtf = Conversions.NormalizeEol(value);
					else
						_ = Errors.ErrorOccurred($"Can only set RichText on a RichEdit control. Attempted on a {_control?.GetType().Name ?? "null"} control.");
				}
			}

			public string Type => typename;

			public object Visible
			{
				get => _control.Visible;
				set => _control.Visible = Options.OnOff(value) ?? false;
			}

			public object BackColor
			{
				get => (_control.BackColor.ToArgb() & 0x00FFFFFF).ToString("X6");

				set
				{
					if (value is string s)
					{
						if (Conversions.TryParseColor(s, out var c))
							_control.BackColor = c;
					}
					else
						_control.BackColor = Color.FromArgb((int)(value.Al() | 0xFF000000));

					if (ParentForm.Visible == true)
						_control.Refresh();
				}
			}

			public object UseTab(object value = null, object exactMatch = null)
			{
				if (_control is KeysharpTabControl tc)
				{
					if (gui == null || !gui.TryGetTarget(out var g))
						return DefaultObject;

					var val = value;
					var exact = exactMatch.Ab();

					if (val is string s)
					{
						if (s.Length > 0)
						{
							if (tc.FindTab(s, exact) is TabPage tp)
							{
								g.CurrentTab = tp;
								g.LastContainer = tp;
							}
							else
								return Errors.ErrorOccurred($"No tab matching the name \"{s}\" found");
						}
					}
					else if (val != null)
					{
						var i = (int)val.Al();
						i--;

						if (i >= 0 && i < tc.TabPages.Count)
						{
							var tp = tc.TabPages[i];
							g.CurrentTab = tp;
							g.LastContainer = tp;
						}
						else
							return Errors.ErrorOccurred($"Tab index {i+1} out of bounds [1..{tc.TabPages.Count}]");
					}
					else
					{
						tc.AdjustSize(((Gui)Gui).DpiScale, requestedSize);
						g.LastContainer = tc.Parent;
					}

					return DefaultObject;
				}

				return Errors.ValueErrorOccurred($"Only Tab controls implement this method.");
			}

			public object Choose(object value)
			{
				//The documentation says "Unlike ControlChooseIndex, this method does not raise a Change or DoubleClick event."
				//But we don't raise click events anyway here, so it shouldn't matter.
				var s = value as string;
				var i = value.Ai() - 1;

				if (_control is KeysharpTabControl tc)
				{
					if (!string.IsNullOrEmpty(s))
					{
						if (tc.FindTab(s, false) is TabPage tp)
							tc.SelectTab(tp);
					}
					else if (i >= 0)
						tc.SelectTab(i);
				}
				else if (_control is KeysharpListBox lb)
				{
					if (!string.IsNullOrEmpty(s))
						lb.SelectItem(s);
					else if (i >= 0)
						lb.SetSelected(i, true);
					else
						lb.ClearSelected();
				}
				else if (_control is KeysharpComboBox cb)
				{
					if (!string.IsNullOrEmpty(s))
						cb.SelectItem(s);
					else if (i >= 0)
						cb.SelectedIndex = i;
					else if (cb.DropDownStyle != ComboBoxStyle.DropDownList)
					{
						cb.SelectedIndex = -1;
						cb.ResetText();
					}
				}

				return DefaultObject;
			}

			public object Focus()
			{
				_control?.Focus();
				return DefaultObject;
			}

			public long Get(object itemID, object attribute)
			{
				if (_control is KeysharpTreeView tv)
				{
					var id = itemID.Al();
					var attr = attribute.As().Trim();

					if (attr.Length > 0 && TreeViewHelper.TV_FindNode(tv, id) is TreeNode node)
					{
						if (Options.OptionContains(attr, Keyword_Expand, Keyword_Expanded, Keyword_Expand[0].ToString()) && node.IsExpanded)
							return node.Handle.ToInt64();
						else if (Options.OptionContains(attr, Keyword_Check, Keyword_Checked, Keyword_Checked[0].ToString()) && node.Checked)
							return node.Handle.ToInt64();
						else if (Options.OptionContains(attr, Keyword_Bold, Keyword_Bold[0].ToString()) && node.NodeFont.Bold)
							return node.Handle.ToInt64();
					}
				}

				return 0L;
			}

			public long GetChild(object itemID)
			{
				if (_control is KeysharpTreeView tv)
				{
					var id = itemID.Al();
					var node = TreeViewHelper.TV_FindNode(tv, id);
					return node == null ? 0 : node.Nodes.Count == 0 ? 0L : node.FirstNode.Handle.ToInt64();
				}

				return 0L;
			}

			public object GetClientPos([Optional()][DefaultParameterValue(null)] object outX,
									   [Optional()][DefaultParameterValue(null)] object outY,
									   [Optional()][DefaultParameterValue(null)] object outWidth,
									   [Optional()][DefaultParameterValue(null)] object outHeight)
			{
				GetClientPos(_control, ((Gui)Gui).DpiScale, outX, outY, outWidth, outHeight);
				return DefaultObject;
			}

			public object GetNode(object itemID)
			{
				if (_control is KeysharpTreeView tv)
				{
					var id = itemID.Al();
					return TreeViewHelper.TV_FindNode(tv, id);
				}

				return DefaultObject;
			}

			public long GetParent(object itemID)
			{
				if (_control is KeysharpTreeView tv)
				{
					var id = itemID.Al();
					var node = TreeViewHelper.TV_FindNode(tv, id);
					return node == null || node.Parent == null ? 0L : (node.Parent is TreeNode tn ? tn.Handle.ToInt64() : 0L);
				}

				return DefaultErrorLong;
			}

			public object GetPos([Optional()][DefaultParameterValue(null)] object outX,
								 [Optional()][DefaultParameterValue(null)] object outY,
								 [Optional()][DefaultParameterValue(null)] object outWidth,
								 [Optional()][DefaultParameterValue(null)] object outHeight)
			{
				GetPos(_control, ((Gui)Gui).DpiScale, outX, outY, outWidth, outHeight);
				return DefaultObject;
			}

			public long GetPrev(object itemID)
			{
				if (_control is KeysharpTreeView tv)
				{
					var id = itemID.Al();
					var node = TreeViewHelper.TV_FindNode(tv, id);
					return node == null || node.PrevNode == null ? 0L : node.PrevNode.Handle.ToInt64();
				}

				return DefaultErrorLong;
			}

			public long GetSelection() => _control is KeysharpTreeView tv&& tv.SelectedNode != null ? tv.SelectedNode.Handle.ToInt64() : 0L;

			public string GetText(object rowNumber, object columnNumber = null)
			{
				if (_control is KeysharpTreeView tv)
				{
					var id = rowNumber.Al();
					var node = TreeViewHelper.TV_FindNode(tv, id);

					if (node != null)
						return node.Text;
				}
				else if (_control is KeysharpListView lv)
				{
					var row = rowNumber.Ai();
					var col = columnNumber.Ai(1);
					row--;
					col = Math.Max(col - 1, 0);

					if (row < 0 && col < lv.Columns.Count)
						return lv.Columns[col].Text;
					else if (row < lv.Items.Count && col < lv.Items[row].SubItems.Count)
						return lv.Items[row].SubItems[col].Text;
				}

				return DefaultErrorString;
			}
			public object SetFont(object options = null, object fontName = null)
			{
				_control.SetFont(options, fontName);
				return DefaultObject;
			}

			public object SetFormat(object format)
			{
				(_control as DateTimePicker)?.SetFormat(format);
				return DefaultObject;
			}

			/// <summary>
			/// The script object standing for a toolkit parent: the wrapping <see cref="Gui.Control"/> when the
			/// parent is a container control (a GroupBox or Tab page), or the owning <see cref="Gui"/> when it is
			/// the window itself. Used by the platform <c>Parent</c> property so that reading it yields a value a
			/// script can use and assign back, rather than the underlying toolkit control.
			/// </summary>
			/// <param name="parent">The toolkit parent, which may be null.</param>
			/// <returns>The Gui.Control or Gui for parent, or an empty value when it maps to neither.</returns>
			internal static object ParentObject(Forms.Control parent)
			{
				if (parent == null)
					return DefaultObject;

				if (parent.GetGuiControl() is Gui.Control gc)
					return gc;

				if (parent is KeysharpForm ksf && ksf.Tag is WeakReference<Gui> wr && wr.TryGetTarget(out var gui))
					return gui;

				return DefaultObject;
			}

			internal static void GetClientPos(Forms.Control control, double scale, [ByRef] object outX, [ByRef] object outY, [ByRef] object outWidth, [ByRef] object outHeight) => GetPosHelper(control, scale, true, outX, outY, outWidth, outHeight);

			internal static void GetPos(Forms.Control control, double scale, [ByRef] object outX, [ByRef] object outY, [ByRef] object outWidth, [ByRef] object outHeight) => GetPosHelper(control, scale, false, outX, outY, outWidth, outHeight);

			internal static void GetPosHelper(Forms.Control control, double scale, bool client, [ByRef] object outX, [ByRef] object outY, [ByRef] object outWidth, [ByRef] object outHeight)
			{
				Rectangle rect;
				if (client)
				{
					// The client area's on-screen rectangle (client origin mapped to the screen, plus the client
					// size). Mirrors AHK's GetClientPos. The per-toolkit mapping lives in GetClientScreenRect.
					rect = control.GetClientScreenRect();
				}
				else
				{
					rect = control.GetBounds();
					if (control?.Parent != null)
					{
						Point p = control.Parent.GetLocationRelativeToForm();
						rect.X += p.X; rect.Y += p.Y;
					}
				}

				if (Math.Abs(scale - 1.0) < 0.0001)
				{
					if (outX != null) Script.SetPropertyValue(outX, "__Value", (long)rect.X);
					if (outY != null) Script.SetPropertyValue(outY, "__Value", (long)rect.Y);
					if (outWidth != null) Script.SetPropertyValue(outWidth, "__Value", (long)rect.Width);
					if (outHeight != null) Script.SetPropertyValue(outHeight, "__Value", (long)rect.Height);
				}
				else
				{
					var inverse = 1.0 / scale;
					// Screen positions stay in the platform's native virtual-desktop space. Only control-local authored
					// positions and GUI sizes use the window's per-monitor scale.
					var localPosition = !client && control?.Parent != null;
					if (outX != null) Script.SetPropertyValue(outX, "__Value", localPosition
						? (long)Math.Ceiling(rect.X * inverse) : (long)rect.X);
					if (outY != null) Script.SetPropertyValue(outY, "__Value", localPosition
						? (long)Math.Ceiling(rect.Y * inverse) : (long)rect.Y);
					if (outWidth != null) Script.SetPropertyValue(outWidth, "__Value", (long)Math.Ceiling(rect.Width * inverse));
					if (outHeight != null) Script.SetPropertyValue(outHeight, "__Value", (long)Math.Ceiling(rect.Height * inverse));
				}
			}

			internal void _control_DoubleClick(object sender, EventArgs e)
			{
				if (!eventHandlerActive)
					return;

				if (_control is KeysharpTreeView tv)
					_ = doubleClickHandlers.InvokeEventHandlers(this, GetSelection());
				else if (_control is KeysharpListView lv)
				{
					if (lv.SelectedIndices.Count > 0)
						_ = doubleClickHandlers.InvokeEventHandlers(this, lv.SelectedIndices[0] + 1L);
					else
						_ = doubleClickHandlers.InvokeEventHandlers(this, 0L);
				}
				else if (_control is KeysharpListBox lb)
				{
					if (lb.SelectedIndices.Count > 0)
						_ = doubleClickHandlers.InvokeEventHandlers(this, lb.SelectedIndices[0] + 1L);
					else
						_ = doubleClickHandlers.InvokeEventHandlers(this, 0L);
				}
				else
					_ = doubleClickHandlers.InvokeEventHandlers(this, 0L);

				//Status strip items are handled in a separate special handler contained within each item.
			}

			internal void _control_GotFocus(object sender, EventArgs e)
			{
				if (eventHandlerActive)
					_ = (focusHandlers?.InvokeEventHandlers(this, 0L));
			}

			internal void _control_LostFocus(object sender, EventArgs e)
			{
				if (eventHandlerActive)
					_ = (lostFocusHandlers?.InvokeEventHandlers(this, 0L));
			}

			internal void CallContextMenuChangeHandlers(bool wasRightClick, int x, int y)
			{
				if (!eventHandlerActive)
					return;

				if (_control is KeysharpListBox lb)
					_ = (contextMenuChangedHandlers?.InvokeEventHandlers(this, lb.SelectedIndex + 1L, wasRightClick, (long)x, (long)y));
				else if (_control is KeysharpListView lv)
					_ = (contextMenuChangedHandlers?.InvokeEventHandlers(this, lv.SelectedIndices.Count > 0 ? lv.SelectedIndices[0] + 1L : 0L, wasRightClick, (long)x, (long)y));
				else if (_control is KeysharpTreeView tv)
					_ = (contextMenuChangedHandlers?.InvokeEventHandlers(this, tv.SelectedNode?.Handle.ToInt64() ?? 0, wasRightClick, (long)x, (long)y));
				else
					_ = (contextMenuChangedHandlers?.InvokeEventHandlers(this, _control.Handle.ToInt64().ToString(), wasRightClick, (long)x, (long)y));//Unsure what to pass for Item, so just pass handle.
			}

			internal void Cmb_SelectedIndexChanged(object sender, EventArgs e)
			{
				if (eventHandlerActive && _control is KeysharpComboBox)
					_ = (changeHandlers?.InvokeEventHandlers(this, 0L));
			}

			internal void Dtp_ValueChanged(object sender, EventArgs e)
			{
				if (eventHandlerActive && _control is KeysharpDateTimePicker)
					_ = (changeHandlers?.InvokeEventHandlers(this, 0L));
			}
			internal void Hkb_TextChanged(object sender, EventArgs e)
			{
				if (eventHandlerActive && _control is HotkeyBox)
					_ = (changeHandlers?.InvokeEventHandlers(this, 0L));
			}
			internal void Lb_SelectedIndexChanged(object sender, EventArgs e)
			{
				if (eventHandlerActive && _control is KeysharpListBox)
					_ = (changeHandlers?.InvokeEventHandlers(this, 0L));
			}

			internal void Lv_SelectedIndexChanged(object sender, EventArgs e)
			{
				if (eventHandlerActive && _control is KeysharpListView lv)
					_ = (focusedItemChangedHandlers?.InvokeEventHandlers(this, lv.SelectedIndices.Count > 0 ? lv.SelectedIndices[0] + 1L : 0L));
			}

			internal void Nud_ValueChanged(object sender, EventArgs e)
			{
				if (eventHandlerActive && _control is KeysharpNumericUpDown)
					_ = (changeHandlers?.InvokeEventHandlers(this, 0L));
			}

			internal void Tb_MouseCaptureChanged(object sender, EventArgs e)
			{
				if (eventHandlerActive && _control is KeysharpTrackBar && !AltSubmit)
					_ = (changeHandlers?.InvokeEventHandlers(this, 0L));//Winforms doesn't support the ability to pass the method by which the slider was changed.
			}

			internal void Tb_ValueChanged(object sender, EventArgs e)
			{
				if (eventHandlerActive && _control is KeysharpTrackBar && AltSubmit)
					_ = (changeHandlers?.InvokeEventHandlers(this, 0L));//Winforms doesn't support the ability to pass the method by which the slider was changed.
			}


			internal void Tc_Selected(object sender, EventArgs e)
			{
				if (eventHandlerActive && _control is KeysharpTabControl)
					_ = (changeHandlers?.InvokeEventHandlers(this, 0L));
			}

			internal void Mc_DateChanged(object sender, EventArgs e)
			{
				if (eventHandlerActive && _control is KeysharpMonthCalendar)
					_ = (changeHandlers?.InvokeEventHandlers(this, 0L));
			}
		}
	}
}
