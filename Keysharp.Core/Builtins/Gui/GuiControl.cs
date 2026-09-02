using Keysharp.Runtime;

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
			private readonly CallbackRegistry clickHandlers = new();
			private readonly CallbackRegistry doubleClickHandlers = new();
			internal bool DpiScaling => ((Gui)Gui).DpiScale != 1.0;
			private Forms.Control _control;

			// Normal event handlers can't be used because they need to return a value.
			// The returned values are then inspected to determine whether subsequent handlers should be called.
			private CallbackRegistry changeHandlers;
			private CallbackRegistry columnClickHandlers;
#if WINDOWS
			private ConcurrentDictionary<int, CallbackRegistry> commandHandlers;
#endif
			private CallbackRegistry contextMenuChangedHandlers;
#if WINDOWS
			private nint dummyHandle;
#endif
			private CallbackRegistry focusedItemChangedHandlers;
			private CallbackRegistry focusHandlers;
			private CallbackRegistry itemCheckHandlers;
			private CallbackRegistry itemEditHandlers;
			private CallbackRegistry itemExpandHandlers;
			private CallbackRegistry lostFocusHandlers;
			//Keyed by window message number, for OnMessage(). Separate from commandHandlers/notifyHandlers
			//because those key on a WM_COMMAND notification code / WM_NOTIFY code, not on the message itself.
			private ConcurrentDictionary<int, CallbackRegistry> messageHandlers;
#if WINDOWS
			private ConcurrentDictionary<int, CallbackRegistry> notifyHandlers;
#endif
			private long parenthandle;
			private CallbackRegistry selectedItemChangedHandlers;
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
				removedAny |= CallbackRegistry.RemoveOwned(commandHandlers, scheduler);
#endif
				removedAny |= contextMenuChangedHandlers?.RemoveOwned(scheduler) == true;
				removedAny |= focusedItemChangedHandlers?.RemoveOwned(scheduler) == true;
				removedAny |= focusHandlers?.RemoveOwned(scheduler) == true;
				removedAny |= itemCheckHandlers?.RemoveOwned(scheduler) == true;
				removedAny |= itemEditHandlers?.RemoveOwned(scheduler) == true;
				removedAny |= itemExpandHandlers?.RemoveOwned(scheduler) == true;
				removedAny |= lostFocusHandlers?.RemoveOwned(scheduler) == true;
				removedAny |= CallbackRegistry.RemoveOwned(messageHandlers, scheduler);
#if WINDOWS
				removedAny |= CallbackRegistry.RemoveOwned(notifyHandlers, scheduler);
#endif
				removedAny |= selectedItemChangedHandlers?.RemoveOwned(scheduler) == true;
				removedAny |= (this as WebView)?.RemoveOwnedWebViewHandlers(scheduler) == true;
				removedAny |= (this as RichEdit)?.RemoveOwnedRichEditHandlers(scheduler) == true;

#if WINDOWS
				if (_control is StatusStrip ss)
				{
					foreach (var item in ss.Items)
					{
						if (item is KeysharpToolStripStatusLabel tssl)
							removedAny |= tssl.doubleClickHandlers.RemoveOwned(scheduler);
					}
				}
#endif

				return removedAny;
			}

			/// <summary>
			/// Whether this control type raises the named event, per the "Applies to" lists in the AHK OnEvent
			/// documentation. AHK throws for an unsupported pair and scripts rely on that: FindText registers
			/// "Click" in a Try and falls back to "Change" in the Catch, so accepting the pair silently left
			/// Change-only controls (sliders, list boxes, drop-downs) with no handler at all. Tested against
			/// the holder type because the widgets are not one-per-control-type (DDL and ComboBox share one).
			/// </summary>
			private bool SupportsEvent(string e) =>
				//No AHK counterpart, so no documented event set to hold them to.
				this is ActiveX
#if WINDOWS
				|| this is Custom
#endif
				|| e switch
			{
				//A WebView's own events, which no other control raises. Its clicks belong to the page, so it
				//is absent from the click/doubleclick sets below.
				_ when WebView.IsWebViewEvent(e) => this is WebView,
				//A RichEdit's own events; no other control has a caret to move or a link to click.
				_ when RichEdit.IsRichEditEvent(e) => this is RichEdit,
				"change" => this is ComboBox or DDL or ListBox or Tab or Edit or RichEdit or DateTime or MonthCal or Hotkey or UpDown or Slider,
				//Gui.Text spelled out: a bare "Text" binds to the Control.Text property instead.
				"click" => this is Keysharp.Builtins.Gui.Text or Pic or Button or CheckBox or Radio or ListView or TreeView or Link or StatusBar,
				"doubleclick" => this is Keysharp.Builtins.Gui.Text or Pic or Button or CheckBox or Radio or ComboBox or ListBox or ListView or TreeView or StatusBar,
				"colclick" => this is ListView,
				//"All controls except Edit and MonthCal, which have their own standard context menu."
				"contextmenu" => this is not (Edit or RichEdit or MonthCal),
				//Only the four the documentation rejects outright; the rest accept and may simply never fire.
				"focus" or "losefocus" => this is not (Hotkey or Slider or Tab or Link),
				"itemcheck" or "itemedit" or "itemselect" => this is ListView or TreeView,
				"itemexpand" => this is TreeView,
				"itemfocus" => this is ListView,
				_ => false,//An event name that is not one of AHK's at all, rejected as AHK does.
			};

			/// <summary>
			/// Registers a function to be called when the control raises the named event.
			/// </summary>
			public object OnEvent(object eventName, object callback, object addRemove = null)
			{
				var e = eventName.As().ToLower();
				var h = callback;
				var i = addRemove.Al(1);

				if (gui == null || !gui.TryGetTarget(out var g))
					return Errors.ErrorOccurred("GUI control's parent GUI is no longer available.");

				var del = KeysharpForm.ResolveHandler(h, g.form.eventObj);

				//ModifyEventHandlers ignores a null delegate, so a callback that did not resolve would otherwise
				//register nothing and still report success, exactly as HandleOnCommandNotify used to.
				if (del == null)
					return Errors.ValueErrorOccurred("The callback was not a valid function.");

				//Checked once here rather than per branch, so the branches below can assume the pair is valid.
				if (!SupportsEvent(e))
					return Errors.ValueErrorOccurred($"A {Type} control does not support the {eventName.As()} event.");

				if (this is WebView wv && WebView.IsWebViewEvent(e))
				{
					wv.ModifyEventHandlers(e, del, i);
				}
				else if (this is RichEdit re && RichEdit.IsRichEditEvent(e))
				{
					re.ModifyEventHandlers(e, del, i);
				}
				else if (e == "change")
				{
					if (changeHandlers == null)
						changeHandlers = new();

					changeHandlers.ModifyEventHandlers(del, i);
				}
				else if (e == "click")
				{
					if (_control is KeysharpLinkLabel ll && !ll.clickSet)
					{
						ll.clickSet = true;
						clickHandlers.Clear();
					}

					clickHandlers.ModifyEventHandlers(del, i);
				}
				else if (e == "doubleclick")
				{
#if WINDOWS

					//A StatusBar raises its double-click per part rather than on the strip itself.
					if (_control is ToolStrip ts)
					{
						foreach (var item in ts.Items)
							if (item is KeysharpToolStripStatusLabel tssl)
								tssl.doubleClickHandlers.ModifyEventHandlers(del, i);
					}
					else
						doubleClickHandlers.ModifyEventHandlers(del, i);

#else
					doubleClickHandlers.ModifyEventHandlers(del, i);
#endif
				}
				else if (e == "focus")
				{
					if (focusHandlers == null)
						focusHandlers = new();

					focusHandlers.ModifyEventHandlers(del, i);
				}
				else if (e == "losefocus")
				{
					if (lostFocusHandlers == null)
						lostFocusHandlers = new();

					lostFocusHandlers.ModifyEventHandlers(del, i);
				}
				else if (e == "colclick")
				{
					if (columnClickHandlers == null)
						columnClickHandlers = new();

					columnClickHandlers.ModifyEventHandlers(del, i);
				}
				else if (e == "itemcheck")
				{
					if (itemCheckHandlers == null)
						itemCheckHandlers = new();

					itemCheckHandlers.ModifyEventHandlers(del, i);
				}
				else if (e == "itemedit")
				{
					if (itemEditHandlers == null)
						itemEditHandlers = new();

					itemEditHandlers.ModifyEventHandlers(del, i);
				}
				else if (e == "itemexpand")
				{
					if (itemExpandHandlers == null)
						itemExpandHandlers = new();

					itemExpandHandlers.ModifyEventHandlers(del, i);
				}
				else if (e == "itemfocus")
				{
					if (focusedItemChangedHandlers == null)
						focusedItemChangedHandlers = new();

					focusedItemChangedHandlers.ModifyEventHandlers(del, i);
				}
				else if (e == "itemselect")
				{
					if (selectedItemChangedHandlers == null)
						selectedItemChangedHandlers = new();

					selectedItemChangedHandlers.ModifyEventHandlers(del, i);
				}
				else if (e == "contextmenu")
				{
					if (contextMenuChangedHandlers == null)
						contextMenuChangedHandlers = new();

					contextMenuChangedHandlers.ModifyEventHandlers(del, i);
				}

				return DefaultObject;
			}

			/// <summary>
			/// Registers a function to be called when the control receives the specified window message.
			/// </summary>
			/// <param name="msgNumber">The number of the message to monitor.</param>
			/// <param name="callback">The function to call, as <c>Callback(GuiCtrlObj, wParam, lParam, Msg)</c>.
			/// If it returns a non-empty value, that value becomes the message's result and no further
			/// handlers (or default processing) run.</param>
			/// <param name="addRemove">If omitted, defaults to 1. Otherwise 1 to call this callback after any
			/// previously registered ones, -1 to call it before them, or 0 to unregister it.</param>
			/// <remarks>
			/// Off Windows there is no native message queue, so only the input messages EtoMessageSource
			/// synthesizes are ever delivered here; see OnMessage() for that set.
			/// </remarks>
			public object OnMessage(object msgNumber, object callback, object addRemove = null)
			{
				var addremove = addRemove.Al(1L);

				//AHK's GuiType::OnEvent rejects anything outside -1..1: a GUI event can only run one thread
				//at a time, so the parameter is purely an ordering/removal switch, not a thread count.
				if (addremove < -1 || addremove > 1)
					return Errors.ValueErrorOccurred($"Invalid AddRemove value: {addremove}.");

				var msg = msgNumber.Al();
				var result = HandleOnCommandNotify(msg, callback, addremove, ref messageHandlers);
#if !WINDOWS

				//Pointer motion is only watched for while something is listening; see SyncMotionHooks().
				if (msg == Keysharp.Internals.Os.Windows.WindowsAPI.WM_MOUSEMOVE)
					Keysharp.Internals.Window.Unix.EtoMessageSource.SyncMotionHooks(Script.TheScript);

#endif
				return result;
			}

			internal object HandleOnCommandNotify(long code, object callback, long addremove, ref ConcurrentDictionary<int, CallbackRegistry> handlers)
			{
				if (gui == null || !gui.TryGetTarget(out var g))
					return Errors.ErrorOccurred("GUI control's parent GUI is no longer available.");

				var del = KeysharpForm.ResolveHandler(callback, g.form.eventObj);

				//ModifyEventHandlers ignores a null delegate, so a callback that did not resolve would
				//otherwise register nothing and still report success. Gui.OnMessage() already rejects one.
				if (del == null)
					return Errors.ValueErrorOccurred("The callback was not a valid function.");

				if (handlers == null)
					handlers = new();

				var h = handlers.GetOrAdd((int)code, static _ => new());
				h.ModifyEventHandlers(del, addremove);
				return DefaultObject;
			}

			/// <summary>
			/// Whether this control has an OnMessage() handler for the given message. Lets the off-Windows
			/// message source decide whether a toolkit event is worth subscribing to at all.
			/// </summary>
			internal bool HasWindowMessageHandler(int msg)
				=> messageHandlers != null && messageHandlers.TryGetValue(msg, out var handler) && handler?.IsEmpty == false;

			/// <summary>
			/// Runs any OnMessage() handlers registered for this control.
			/// </summary>
			/// <returns>True if a handler claimed the message and supplied its result.</returns>
			internal bool InvokeWindowMessageHandlers(ref Message m)
			{
				//OnMessage() monitors run before the WM_COMMAND/WM_NOTIFY reflection in the caller and before
				//default processing, mirroring AHK's ControlWindowProc (which checks its message monitors ahead
				//of DefSubclassProc). A non-empty return marks the message handled and supplies its result.
				//"Claimed" must be the same test the chain broke on, so reuse that predicate rather than
				//restating it: a NON-EMPTY return claims the message (an explicit 0 replies 0 and suppresses
				//default processing), while "" or no return at all falls through to the next handler and then
				//to the reflection/default handling in the caller.
				if (messageHandlers != null && messageHandlers.TryGetValue(m.Msg, out var msgHandler))
				{
					var msgResult = msgHandler?.InvokeWindowMessageHandlers(this, m.WParam.ToInt64(), m.LParam.ToInt64(), (long)m.Msg);

					if (CallbackStop.NonEmpty(msgResult))
					{
						m.Result = (nint)msgResult.Al();
						return true;
					}
				}

				return false;
			}

			public bool AltSubmit { get; internal set; } = false;
			internal Forms.Control Ctrl => _control;

			/// <summary>
			/// The backing toolkit control as an ordinary <c>Ks.Clr</c> object. Its concrete type is
			/// platform-dependent and unspecified; changes made through it bypass this class's own state and
			/// event wiring.
			/// </summary>
			public object ToClr() => ManagedInvoke.WrapManaged(_control);

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
			/// <summary>
			/// This control's font as a <see cref="Ks.Font"/>. Reading returns a detached copy; assigning
			/// applies only the properties the font sets. See <see cref="Gui.Font"/>.
			/// </summary>
			public object Font
			{
				get => Ks.Font.FromControl(_control.Font, _control.ForeColor);

				set
				{
					if (value is Ks.Font f)
						_control.SetFont(f.Options, f.Name);
					else
						_ = Errors.TypeErrorOccurred(value, typeof(Ks.Font));
				}
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
					if (outX != null) Refs.SetValue(outX, (long)rect.X);
					if (outY != null) Refs.SetValue(outY, (long)rect.Y);
					if (outWidth != null) Refs.SetValue(outWidth, (long)rect.Width);
					if (outHeight != null) Refs.SetValue(outHeight, (long)rect.Height);
				}
				else
				{
					var inverse = 1.0 / scale;
					// Screen positions stay in the platform's native virtual-desktop space. Only control-local authored
					// positions and GUI sizes use the window's per-monitor scale.
					var localPosition = !client && control?.Parent != null;
					if (outX != null) Refs.SetValue(outX, localPosition
						? (long)Math.Ceiling(rect.X * inverse) : (long)rect.X);
					if (outY != null) Refs.SetValue(outY, localPosition
						? (long)Math.Ceiling(rect.Y * inverse) : (long)rect.Y);
					if (outWidth != null) Refs.SetValue(outWidth, (long)Math.Ceiling(rect.Width * inverse));
					if (outHeight != null) Refs.SetValue(outHeight, (long)Math.Ceiling(rect.Height * inverse));
				}
			}

			internal void _control_DoubleClick(object sender, EventArgs e)
			{
				if (!eventHandlerActive)
					return;

				if (_control is KeysharpTreeView tv)
					doubleClickHandlers.InvokeEventHandlers(this, GetSelection());
				else if (_control is KeysharpListView lv)
				{
					if (lv.SelectedIndices.Count > 0)
						doubleClickHandlers.InvokeEventHandlers(this, lv.SelectedIndices[0] + 1L);
					else
						doubleClickHandlers.InvokeEventHandlers(this, 0L);
				}
				else if (_control is KeysharpListBox lb)
				{
					if (lb.SelectedIndices.Count > 0)
						doubleClickHandlers.InvokeEventHandlers(this, lb.SelectedIndices[0] + 1L);
					else
						doubleClickHandlers.InvokeEventHandlers(this, 0L);
				}
#if !WINDOWS
				//Off Windows a StatusBar is one control whose parts are child controls, so the part has to be
				//resolved from where the click landed. On Windows each part raises its own event instead - see
				//the note below.
				else if (_control is KeysharpStatusStrip sbar)
					doubleClickHandlers.InvokeEventHandlers(this, sbar.PartFromPoint());

#endif
				else
					doubleClickHandlers.InvokeEventHandlers(this, 0L);

				//Status strip items are handled in a separate special handler contained within each item.
			}

			internal void _control_GotFocus(object sender, EventArgs e)
			{
				if (eventHandlerActive)
					focusHandlers?.InvokeEventHandlers(this, 0L);
			}

			internal void _control_LostFocus(object sender, EventArgs e)
			{
				if (eventHandlerActive)
					lostFocusHandlers?.InvokeEventHandlers(this, 0L);
			}

			internal object CallContextMenuChangeHandlers(bool wasRightClick, int x, int y, long? itemOverride = null)
			{
				if (!eventHandlerActive)
					return null;

				if (_control is KeysharpListBox lb)
					return contextMenuChangedHandlers?.InvokeWindowMessageHandlers(this, lb.SelectedIndex + 1L, wasRightClick, (long)x, (long)y);
				else if (_control is KeysharpListView lv)
					return contextMenuChangedHandlers?.InvokeWindowMessageHandlers(this,
						itemOverride ?? (lv.SelectedIndices.Count > 0 ? lv.SelectedIndices[0] + 1L : 0L),
						wasRightClick, (long)x, (long)y);
				else if (_control is KeysharpTreeView tv)
					return contextMenuChangedHandlers?.InvokeWindowMessageHandlers(this, tv.SelectedNode?.Handle.ToInt64() ?? 0, wasRightClick, (long)x, (long)y);
				else
					return contextMenuChangedHandlers?.InvokeWindowMessageHandlers(this, _control.Handle.ToInt64().ToString(), wasRightClick, (long)x, (long)y);//Unsure what to pass for Item, so just pass handle.
			}

			internal void Cmb_SelectedIndexChanged(object sender, EventArgs e)
			{
				if (eventHandlerActive && _control is KeysharpComboBox)
					changeHandlers?.InvokeEventHandlers(this, 0L);
			}

			internal void Dtp_ValueChanged(object sender, EventArgs e)
			{
				if (eventHandlerActive && _control is KeysharpDateTimePicker)
					changeHandlers?.InvokeEventHandlers(this, 0L);
			}
			internal void Hkb_TextChanged(object sender, EventArgs e)
			{
				if (eventHandlerActive && _control is HotkeyBox)
					changeHandlers?.InvokeEventHandlers(this, 0L);
			}
			internal void Lb_SelectedIndexChanged(object sender, EventArgs e)
			{
				if (eventHandlerActive && _control is KeysharpListBox)
					changeHandlers?.InvokeEventHandlers(this, 0L);
			}

			internal void Lv_SelectedIndexChanged(object sender, EventArgs e)
			{
				if (eventHandlerActive && _control is KeysharpListView lv)
					focusedItemChangedHandlers?.InvokeEventHandlers(this, lv.SelectedIndices.Count > 0 ? lv.SelectedIndices[0] + 1L : 0L);
			}

			internal void Nud_ValueChanged(object sender, EventArgs e)
			{
				if (eventHandlerActive && _control is KeysharpNumericUpDown)
					changeHandlers?.InvokeEventHandlers(this, 0L);
			}

			internal void Tb_MouseCaptureChanged(object sender, EventArgs e)
			{
				if (eventHandlerActive && _control is KeysharpTrackBar && !AltSubmit)
					changeHandlers?.InvokeEventHandlers(this, 0L);//Winforms doesn't support the ability to pass the method by which the slider was changed.
			}

			internal void Tb_ValueChanged(object sender, EventArgs e)
			{
				if (eventHandlerActive && _control is KeysharpTrackBar && AltSubmit)
					changeHandlers?.InvokeEventHandlers(this, 0L);//Winforms doesn't support the ability to pass the method by which the slider was changed.
			}


			internal void Tc_Selected(object sender, EventArgs e)
			{
				if (eventHandlerActive && _control is KeysharpTabControl)
					changeHandlers?.InvokeEventHandlers(this, 0L);
			}

			internal void Mc_DateChanged(object sender, EventArgs e)
			{
				if (eventHandlerActive && _control is KeysharpMonthCalendar)
					changeHandlers?.InvokeEventHandlers(this, 0L);
			}
		}
	}
}
