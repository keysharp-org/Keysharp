#if !WINDOWS
namespace Keysharp.Core
{
	public partial class Gui : KeysharpObject
	{
		public partial class Control : KeysharpObject
		{
			private HashSet<int> listViewSelectedRows;
			private int listViewFocusedRow = -1;
			private bool listViewCheckClickActive;
			public string ClassNN => _control is Window && WindowManager.CreateWindow(_control.Handle) is WindowItemBase wi ? wi.ClassNN : "";

			public object Gui => gui != null && gui.TryGetTarget(out var g) ? g : DefaultErrorObject;

			public long Hwnd => _control is Eto.Widget w ? w.Handle.ToInt64() : 0L;

			public string NetClassNN
			{
				get
				{
					if (Hwnd == 0)
						return "";
					return WindowManager.CreateWindow((nint)Hwnd) is WindowItemBase wi ? wi.NetClassNN : "";
				}
			}

			public object Parent
			{
				get => _control?.Parent;
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
						return DefaultErrorObject;

					if (_control is KeysharpListBox lb)
					{
						if (lb.SelectionMode == SelectionMode.One)
						{
							if (lb.SelectedIndices.Count > 0 && lb.SelectedItem is string s)
								return s;
						}
						else
							return new Array(lb.SelectedItems);
					}

					if (_control is KeysharpComboBox cb)
					{
						if (cb.DropDownStyle == ComboBoxStyle.DropDownList && cb.SelectedIndex > 0 && cb.SelectedItem is string s)
							return s;
						else
							return cb.Text;
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

					var s = value.As();

					if (_control is KeysharpListBox lb)
					{
						if (lb.SelectionMode == SelectionMode.One)
						{
							lb.SelectedItem = s;
						}
						else
						{
							lb.SelectedItems.Clear();
							lb.SelectedIndices.Clear();
							for (var i = 0; i < lb.Items.Count; i++)
							{
								if (lb.Items[i] is string item && item == s)
									lb.SetSelected(i, true);
							}
						}
					}
					else if (_control is KeysharpComboBox cb)
					{
						if (s?.Length == 0)
							cb.SelectedIndex = -1;

						if (cb.DropDownStyle == ComboBoxStyle.DropDownList)
							cb.SelectedItem = s;
						else
							cb.Text = s;
					}
					else if (_control is KeysharpTabControl tc)
						tc.SelectTab(s);
					else if (_control is KeysharpGroupBox gb)
						gb.Text = s;
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
						return lb.SelectionMode == SelectionMode.One
							   ? lb.SelectedIndices.Count > 0 ? (long)lb.SelectedIndices[0] + 1 : 0L
							   : new Array(lb.SelectedIndices.Cast<int>().Select(x => (long)x + 1));
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
					else if (_control is KeysharpStatusStrip ss)
						return ss.Text;//Unsure if this is what's intended.
					else if (_control is KeysharpPictureBox pic)
						return pic.Filename;
					else if (_control is TextControl ctrl)
						return KeysharpEnhancements.NormalizeEol(ctrl.Text);

					return DefaultObject;
				}
				set
				{
					var val = value != null ? value.ToString() : "";
					var temp = value.ParseInt(false);
					var ival = temp ?? 0;

					if (_control is KeysharpNumericUpDown nud)
						nud.Value = (double)value.ParseDecimal().Value;
					else if (_control is KeysharpCheckBox cb)
					{
						var cbstate = ival;

						if (cbstate == -1)
							cb.CheckState = CheckState.Indeterminate;
						else
						{
							cb.Checked = Options.OnOff(value) ?? false;
							cb.CheckState = cb.Checked == true ? CheckState.Checked : CheckState.Unchecked;
						}
					}
					else if (_control is KeysharpRadioButton rb)
						rb.Checked = Options.OnOff(value) ?? false;
					else if (_control is KeysharpComboBox cmb)
						cmb.SelectedIndex = ival - 1;
					else if (_control is KeysharpListBox lb)
					{
						if (value is Array ar)
						{
							lb.SelectedItems.Clear();
							lb.SelectedIndices.Clear();

							foreach (var arval in ar)
								lb.SetSelected(arval.ParseInt().GetValueOrDefault() - 1, true);
						}
						else
						{
							lb.SelectedItems.Clear();
							lb.SelectedIndices.Clear();
							var index = ival - 1;
							if (index >= 0 && index < lb.Items.Count)
								lb.SelectedItem = lb.Items[index];
						}
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
						tb.Value = ival;
					else if (_control is KeysharpProgressBar pb)
						pb.Value = ival;
					else if (_control is KeysharpTabControl tc)
						tc.SelectedIndex = ival - 1;
					else if (_control is KeysharpStatusStrip ss)
						ss.Text = val;
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

							if (width == int.MinValue)
								width = pic.Width;
							if (height == int.MinValue)
								height = pic.Height;

							if (ImageHelper.LoadImage(filename, width, height, iconnumber).Item1 is Bitmap bmp)
							{
								var oldimage = pic.Image;
								pic.Image = bmp;

								if (oldimage is Bitmap oldbmp)
									oldbmp.Dispose();
							}
						}
					}
					else if (_control is TextControl ctrl)
						ctrl.Text = KeysharpEnhancements.NormalizeEol(val, Environment.NewLine);
					else if (_control != null)
						_control.Text = val;

					if (ParentForm?.Visible == true)
						_control.Invalidate();
				}
			}

			public override object __New(params object[] args)
			{
				if (args.Length == 0)
					return DefaultErrorObject;

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
					return DefaultObject;

				_control.MouseDown += _control_Click;
				_control.MouseDoubleClick += _control_DoubleClick;

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
					lv.KeyDown += Lv_KeyDownEdit;
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
				else if (_control is TextControl txt)
				{
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
				_control.MouseDown += _control_MouseDown;

				return DefaultObject;
			}

			public object Add(params object[] obj)
			{
				object result = DefaultObject;
				eventHandlerActive = false;

				try
				{
					if (_control is KeysharpTreeView tv)
					{
						var (name, parent, options) = obj.Sls();
						var first = false;
						var sort = false;
						var insertAfterId = long.MinValue;
						TreeNode node;
						TreeNodeCollection nodes = null;
						parenthandle = tv.Handle.ToInt64();//By forcing a read of the parent handle, it causes the controls and their handles to properly be created. This is the only way nodes will be properly nested before the window is shown.

						if (parent == 0)
						{
							nodes = tv.Nodes;
						}
						else
						{
							var top = TreeViewHelper.TV_FindNode(tv, parent);
							nodes = top == null ? tv.Nodes : top.Nodes;
						}

						foreach (Range r in options.AsSpan().SplitAny(Spaces))
						{
							var opt = options.AsSpan(r).Trim();

							if (opt.Length > 0)
							{
								if (long.TryParse(opt, out var index)) { insertAfterId = index; }
								else if (opt.Equals("First", StringComparison.OrdinalIgnoreCase)) { first = true; }
								else if (opt.Equals("Sort", StringComparison.OrdinalIgnoreCase)) { sort = true; }
							}
						}

						if (first)
							node = nodes.Insert(0, name);
						else if (sort)
						{
							var insertAt = nodes.Count;
							for (var i = 0; i < nodes.Count; i++)
							{
								if (string.Compare(name, nodes[i].Text, StringComparison.OrdinalIgnoreCase) < 0)
								{
									insertAt = i;
									break;
								}
							}
							node = nodes.Insert(insertAt, name);
						}
						else if (insertAfterId != long.MinValue)
						{
							var insertAt = nodes.Count;
							for (var i = 0; i < nodes.Count; i++)
							{
								if (nodes[i].Handle.ToInt64() == insertAfterId)
								{
									insertAt = i + 1;
									break;
								}
							}
							node = nodes.Insert(insertAt, name);
						}
						else
							node = nodes.Add(name);

						tv.DelayedExpandParent(node);
						var id = node.Handle.ToInt64();
						node.Name = id.ToString();
						result = TreeViewHelper.TV_NodeOptions(node, parent, options, false);
					}
				else if (_control is KeysharpListView lv)
				{
					var options = obj.Length > 0 && obj[0] is string s ? s : null;
					var lvo = ParseListViewOptions(options);
					var startIndex = options != null || obj.Length > 0 && obj[0] == null ? 1 : 0;
					var strs = obj.Cast<object>().Skip(startIndex).Select(x => x.Str()).ToList();
					var rowIndex = lv.AddRow(strs, lvo.ischecked ?? false, lvo.colstart);
					var grid = (Eto.Forms.GridView)lv;
					var rowNumber = rowIndex - 1;
					var selectionChanged = false;

					if (lvo.select.HasValue)
					{
						if (lvo.select.Value)
						{
							if (lv.MultiSelect)
								grid.SelectRow(rowNumber);
							else
								grid.SelectedRow = rowNumber;
						}
						else
						{
							if (lv.MultiSelect)
								grid.UnselectRow(rowNumber);
							else if (grid.SelectedRow == rowNumber)
								grid.SelectedRow = -1;
						}
						selectionChanged = true;
					}

					if (lvo.focused.HasValue)
					{
						if (lvo.focused.Value)
						{
							lv.FocusedItem = lv.Items[rowNumber];
							listViewFocusedRow = rowNumber;
						}
						else if (listViewFocusedRow == rowNumber)
						{
							lv.FocusedItem = null;
							listViewFocusedRow = -1;
						}
						selectionChanged = true;
					}

					if (lvo.vis)
						grid.ScrollToRow(rowNumber);

					if (selectionChanged)
						Lv_SelectedRowsChanged(lv, EventArgs.Empty);

					result = rowIndex;
				}
					else
					{
						if (obj.Length > 0 && obj[0] is Array arr)
							obj = arr.array.ToArray();

						if (_control is KeysharpListBox lb)//Using AddRange() relieves the caller of having to set -Redraw first.
							lb.Items.AddRange(obj.Cast<object>().Select(x => x.Str()).ToArray());
						else if (_control is KeysharpComboBox cb)
							cb.Items.AddRange(obj.Cast<object>().Select(x => x.Str()).ToArray());
						else if (_control is KeysharpTabControl tc)
							tc.TabPages.AddRange(obj.Cast<object>().Select(x => new TabPage(x.Str())).ToArray());
					}
				}
				finally
				{
					eventHandlerActive = true;
				}

				return result;
			}

			public long Delete(object value = null)
			{
				var index = value.Ai() - 1;

				switch (_control)
				{
					case KeysharpListBox lb:
						if (index >= 0)
							lb.Items.RemoveAt(index);
						else
							lb.Items.Clear();

						return 1L;

					case KeysharpComboBox cb:
						if (index >= 0)
							cb.Items.RemoveAt(index);
						else
							cb.Items.Clear();

						return 1L;

					case KeysharpTabControl tc:
					{
						if (index < 0) 
						{
							index = tc.TabPages.Count + index;
							if (index >= 0)
								tc.TabPages.RemoveAt(index);
							else
								index = 0;
						}
						else if (index < tc.TabPages.Count)
						{
							tc.TabPages.RemoveAt(index);
						}

						return (long)index;
					}

					case KeysharpTreeView tv:
					{
						var id = value.Al(long.MinValue);

						if (id == long.MinValue)
						{
							tv.Nodes.Clear();
							tv.SelectedNode = null;
							tv.TopNode = null;
							tv.ReloadData();
							return 1L;
						}
						else if (TreeViewHelper.TV_FindNode(tv, id) is TreeNode node)
						{
							node.Remove();
							if (ReferenceEquals(tv.SelectedNode, node))
								tv.SelectedNode = null;
							tv.ReloadData();
							return 1L;
						}

						break;
					}

					case KeysharpListView lv:
						if (index < 0)
						{
							lv.Items.Clear();
							lv.RefreshDataStore();
							lv.SelectedItems.Clear();
							lv.SelectedIndices.Clear();
							lv.FocusedItem = null;
							listViewSelectedRows = [];
							listViewFocusedRow = -1;
							return 1L;
						}
						else if (index < lv.Items.Count)
						{
							lv.Items.RemoveAt(index);
							lv.RefreshDataStore();
							lv.SelectedItems.Clear();
							lv.SelectedIndices.Clear();
							lv.FocusedItem = null;
							listViewSelectedRows = [];
							listViewFocusedRow = -1;
							return 1L;
						}

						break;
				}

				return 0L;
			}

			public long DeleteCol(object column)
			{
				if (_control is KeysharpListView lv)
				{
					var index = column.Ai() - 1;

					if (index >= 0 && index < lv.Columns.Count)
					{
						var oldCount = lv.Columns.Count;
						lv.Columns.RemoveAt(index);
						lv.RemoveColumnState(index);
						var newCount = lv.Columns.Count;

						foreach (var item in lv.Items)
						{
							var values = new List<string>(oldCount);
							for (var i = 0; i < oldCount; i++)
								values.Add(KeysharpListView_GetCellText(item, i));

							if (index < values.Count)
								values.RemoveAt(index);

							KeysharpListView_EnsureSubItems(item, newCount);
							for (var i = 0; i < newCount; i++)
								KeysharpListView_SetCellText(item, i, values[i]);
						}

						lv.SyncColumns();
						return 1L;
					}
				}

				return 0L;
			}

			public long GetCount(object mode = null)
			{
				if (_control is KeysharpListView lv)
				{
					var m = mode.As();

					if (m?.Length == 0)
						return lv.Items.Count;
					else if (m.StartsWith("s", StringComparison.OrdinalIgnoreCase))
						return lv.SelectedItems.Count;
					else if (m.StartsWith("c", StringComparison.OrdinalIgnoreCase))
						return lv.Columns.Count;
				}
				else if (_control is KeysharpTreeView tv)
				{
					var count = 0L;
					foreach (var _ in EnumerateNodes(tv.Nodes))
						count++;
					return count;
				}

				return 0L;
			}

			public long GetNext(object startingRowNumber = null, object rowType = null)
			{
				var id = startingRowNumber.Al();
				var mode = rowType.As();

				if (_control is KeysharpTreeView tv)
				{
				none:
					if (string.IsNullOrEmpty(mode))
					{
						if (id == 0)
							return tv.Nodes.Count == 0 ? 0L : tv.Nodes[0].Handle.ToInt64();

						var node = TreeViewHelper.TV_FindNode(tv, id);
						return node == null || node.NextNode == null ? 0L : node.NextNode.Handle.ToInt64();
					}

					var modeLower = mode.Trim().ToLowerInvariant();
					var check = modeLower == Keyword_Check || modeLower == Keyword_Checked || modeLower == Keyword_Checked[0].ToString();
					var full = check || modeLower == Keyword_Full || modeLower == Keyword_Full[0].ToString();

					if (!full)
					{
						mode = null;
						goto none;
					}

					var found = id == 0;
					foreach (var node in EnumerateNodes(tv.Nodes))
					{
						if (!found)
						{
							if (node.Handle.ToInt64() == id)
								found = true;
							continue;
						}

						if (check && !node.Checked)
							continue;

						return node.Handle.ToInt64();
					}
				}
				else if (_control is KeysharpListView lv)
				{
					var startrow = (int)(id <= 0 ? 0 : id);
					var modeLower = mode?.Trim() ?? "";
					var c = modeLower.StartsWith("c", StringComparison.OrdinalIgnoreCase);
					var f = modeLower.StartsWith("f", StringComparison.OrdinalIgnoreCase);

					if (c)
					{
						for (var i = startrow; i < lv.Items.Count; i++)
							if (lv.Items[i].Checked)
								return i + 1;
					}
					else if (f)
					{
						if (listViewFocusedRow >= startrow)
							return listViewFocusedRow + 1;

						if (lv.FocusedItem != null)
						{
							var focusedIndex = lv.Items.IndexOf(lv.FocusedItem);
							if (focusedIndex >= startrow)
								return focusedIndex + 1;
						}
					}
					else
					{
						if (listViewSelectedRows != null && listViewSelectedRows.Count > 0)
						{
							var start = id <= 0 ? 1 : (int)id + 1;
							for (var row = start; row <= lv.Items.Count; row++)
								if (listViewSelectedRows.Contains(row))
									return row;
						}
						else
						{
							var grid = (Eto.Forms.GridView)lv;
							var selected = grid.SelectedRows.ToHashSet();
							if (selected.Count == 0 && grid.SelectedRow >= 0)
								selected.Add(grid.SelectedRow);
							for (var i = startrow; i < lv.Items.Count; i++)
								if (selected.Contains(i))
									return i + 1;
						}
					}
				}

				return 0L;
			}

			private static IEnumerable<TreeNode> EnumerateNodes(TreeNodeCollection nodes)
			{
				foreach (var node in nodes)
				{
					yield return node;
					if (node.Nodes.Count > 0)
					{
						foreach (var child in EnumerateNodes(node.Nodes))
							yield return child;
					}
				}
			}
			public long Insert(object rowNumber, params object[] obj)
			{
				if (_control is KeysharpListView lv)
				{
					var index = rowNumber.Ai() - 1;
					var options = obj.Length > 0 && obj[0] is string s ? s : null;
					var lvo = ParseListViewOptions(options);
					var startIndex = options != null || obj.Length > 0 && obj[0] == null ? 1 : 0;
					var strs = obj.Cast<object>().Skip(startIndex).Select(x => x.Str()).ToList();
					var insertedRow = lv.InsertRow(index, strs, lvo.ischecked ?? false, lvo.colstart);
					var grid = (Eto.Forms.GridView)lv;
					var rowNumberIndex = insertedRow - 1;
					var selectionChanged = false;

					if (lvo.select.HasValue)
					{
						if (lvo.select.Value)
						{
							if (lv.MultiSelect)
								grid.SelectRow(rowNumberIndex);
							else
								grid.SelectedRow = rowNumberIndex;
						}
						else
						{
							if (lv.MultiSelect)
								grid.UnselectRow(rowNumberIndex);
							else if (grid.SelectedRow == rowNumberIndex)
								grid.SelectedRow = -1;
						}
						selectionChanged = true;
					}

					if (lvo.focused.HasValue)
					{
						if (lvo.focused.Value)
						{
							lv.FocusedItem = lv.Items[rowNumberIndex];
							listViewFocusedRow = rowNumberIndex;
						}
						else if (listViewFocusedRow == rowNumberIndex)
						{
							lv.FocusedItem = null;
							listViewFocusedRow = -1;
						}
						selectionChanged = true;
					}

					if (lvo.vis)
						grid.ScrollToRow(rowNumberIndex);

					if (selectionChanged)
						Lv_SelectedRowsChanged(lv, EventArgs.Empty);

					return insertedRow;
				}

				return 0L;
			}
			public long InsertCol(object columnNumber = null, object options = null, object columnTitle = null)
			{
				if (_control is KeysharpListView lv)
				{
					var index = columnNumber.Ai(int.MaxValue) - 1;
					var title = columnTitle.As();
					var header = new ColumnHeader
					{
						Text = title ?? ""
					};

					var oldCount = lv.Columns.Count;
					if (index < oldCount)
					{
						if (index < 0)
							index = 0;
						lv.Columns.Insert(index, header);
						lv.InsertColumnState(index);
					}
					else
					{
						index = lv.Columns.Count;
						lv.Columns.Add(header);
						lv.InsertColumnState(index);
					}

					var newCount = lv.Columns.Count;
					if (newCount != oldCount)
					{
						foreach (var item in lv.Items)
						{
							var values = new List<string>(oldCount);
							for (var i = 0; i < oldCount; i++)
								values.Add(KeysharpListView_GetCellText(item, i));

							if (index < 0 || index > values.Count)
								values.Add("");
							else
								values.Insert(index, "");

							KeysharpListView_EnsureSubItems(item, newCount);
							for (var i = 0; i < newCount; i++)
								KeysharpListView_SetCellText(item, i, values[i]);
						}
					}

					lv.SyncColumns();

					var optString = options.As();
					if (!string.IsNullOrEmpty(optString) || !string.IsNullOrEmpty(title))
						_ = ModifyCol(index + 1L, optString, title);

					return index + 1L;
				}

				return -1L;
			}
			public long Modify(object rowNumber, object options = null, params object[] obj)
			{
				var opts = options == null ? null : options.ToString();
				var rownumber = rowNumber.Al();

				if (_control is KeysharpTreeView tv)
				{
					var id = rownumber;
					var name = obj.S1();

					if (TreeViewHelper.TV_FindNode(tv, id) is TreeNode node)
					{
						if (opts?.Length == 0 && name?.Length == 0)
						{
							node.TreeView.SelectedNode = node;
							return node.Handle.ToInt64();
						}
						else if (name != "")
							node.Text = name;

						var parentId = node.Parent is TreeNode parentNode ? parentNode.Handle.ToInt64() : 0L;
						return TreeViewHelper.TV_NodeOptions(node, parentId, opts, true);
					}
				}
				else if (_control is KeysharpListView lv)
				{
					if (lv.Items.Count == 0)
						return 0L;

					var needsRefresh = false;
					var selectionChanged = false;
					var lvo = ParseListViewOptions(opts);

					var strs = obj.Length > 0 ? obj.Cast<object>().Select(x => x.Str()).ToList() : [];
					var start = Math.Max(0, rownumber - 1);
					var end = rownumber == 0 ? lv.Items.Count : Math.Min(rownumber, lv.Items.Count);

					for (rownumber = start; rownumber < end; rownumber++)
					{
						var item = lv.Items[(int)rownumber];

						for (int i = 0, j = lvo.colstart; i < strs.Count && j < item.SubItems.Count; i++, j++)
						{
							item.SubItems[j].Text = strs[i];
							needsRefresh = true;
						}

						if (lvo.ischecked.HasValue)
						{
							item.Checked = lvo.ischecked.Value;
							needsRefresh = true;
						}

						if (lvo.select.HasValue)
						{
							var grid = (Eto.Forms.GridView)lv;
							if (lvo.select.Value)
							{
								if (lv.MultiSelect)
									grid.SelectRow((int)rownumber);
								else
									grid.SelectedRow = (int)rownumber;
							}
							else
							{
								if (lv.MultiSelect)
									grid.UnselectRow((int)rownumber);
								else if (grid.SelectedRow == (int)rownumber)
									grid.SelectedRow = -1;
							}
							selectionChanged = true;
						}

						if (lvo.focused.HasValue)
						{
							if (lvo.focused.Value)
							{
								lv.FocusedItem = item;
								listViewFocusedRow = (int)rownumber;
							}
							else if (listViewFocusedRow == (int)rownumber)
							{
								lv.FocusedItem = null;
								listViewFocusedRow = -1;
							}
							selectionChanged = true;
						}

						if (lvo.vis)
							((Eto.Forms.GridView)lv).ScrollToRow((int)rownumber);
					}

					if ((lvo.focused ?? false) && focusedItemChangedHandlers != null && listViewFocusedRow >= 0)
						_ = focusedItemChangedHandlers.InvokeEventHandlers(this, (long)(listViewFocusedRow + 1));

					if (needsRefresh)
						lv.RefreshDataStore();
					if (selectionChanged)
						Lv_SelectedRowsChanged(lv, EventArgs.Empty);
					return 1L;
				}

				return 0L;
			}
			public long ModifyCol(object columnNumber = null, object options = null, object columnTitle = null)
			{
				if (_control is KeysharpListView lv)
				{
					var index = columnNumber.Ai() - 1;
					var opts = options.As();
					var coltitle = columnTitle.As();
					var width = int.MinValue;
					var auto = false;
					var autoHdr = false;
					Eto.Forms.TextAlignment? align = null;

					if (opts?.Length == 0 && coltitle?.Length == 0)
					{
						if (index < 0)
						{
							lv.AutoResizeColumns(ColumnHeaderAutoResizeStyle.HeaderSize);
							return 1L;
						}
					}

					if (index < 0 || index >= lv.Columns.Count)
						return 0L;

					if (coltitle != "")
					{
						lv.Columns[index].Text = coltitle;
						lv.SyncColumns();
					}

					var sort = false;
					var sortDesc = false;
					var sortMode = "";
					var noSort = false;
					var defaultDesc = false;

					if (!string.IsNullOrEmpty(opts))
					{
						foreach (Range r in opts.AsSpan().SplitAny(Spaces))
						{
							var opt = opts.AsSpan(r).Trim();
							if (opt.Length == 0)
								continue;

							if (opt.Equals("SortDesc", StringComparison.OrdinalIgnoreCase))
								sortDesc = true;
							else if (opt.Equals("Sort", StringComparison.OrdinalIgnoreCase))
								sort = true;
							else if (opt.Equals("Auto", StringComparison.OrdinalIgnoreCase))
								auto = true;
							else if (opt.Equals("AutoHdr", StringComparison.OrdinalIgnoreCase))
								autoHdr = true;
							else if (opt.Equals("Left", StringComparison.OrdinalIgnoreCase))
								align = Eto.Forms.TextAlignment.Left;
							else if (opt.Equals("Center", StringComparison.OrdinalIgnoreCase))
								align = Eto.Forms.TextAlignment.Center;
							else if (opt.Equals("Right", StringComparison.OrdinalIgnoreCase))
								align = Eto.Forms.TextAlignment.Right;
							else if (opt.Equals("NoSort", StringComparison.OrdinalIgnoreCase))
								noSort = true;
							else if (opt.Equals("Desc", StringComparison.OrdinalIgnoreCase))
								defaultDesc = true;
							else if (int.TryParse(opt, out var parsedWidth))
								width = parsedWidth;
							else if (opt.Equals("Integer", StringComparison.OrdinalIgnoreCase))
								sortMode = "Integer";
							else if (opt.Equals("Float", StringComparison.OrdinalIgnoreCase))
								sortMode = "Float";
							else if (opt.Equals("Text", StringComparison.OrdinalIgnoreCase))
								sortMode = "Text";
						}
					}

					if (sortMode.Length > 0)
						lv.SetColumnSortMode(index, sortMode);

					if (noSort)
						lv.SetColumnNoSort(index, true);
					else
						lv.SetColumnNoSort(index, false);

					if (defaultDesc)
						lv.SetColumnSortDefaultDescending(index, true);

					if (sortDesc)
						lv.SortByColumn(index, true);
					else if (sort)
						lv.SortByColumn(index, false);

					if (autoHdr)
						lv.AutoResizeColumn(index, true);
					else if (auto)
						lv.AutoResizeColumn(index, false);

					if (width != int.MinValue)
						lv.SetColumnWidth(index, width);

					if (align.HasValue)
						lv.SetColumnAlignment(index, align.Value);

					return 1L;
				}

				return 0L;
			}

			private sealed class ListViewOptions
			{
				public bool? ischecked;
				public bool? focused;
				public bool? select;
				public bool vis;
				public int colstart;
				public int icon;
			}

			private static ListViewOptions ParseListViewOptions(string options)
			{
				var lvo = new ListViewOptions();

				if (string.IsNullOrEmpty(options))
					return lvo;

				foreach (Range r in options.AsSpan().SplitAny(Spaces))
				{
					var opt = options.AsSpan(r).Trim();
					if (opt.Length == 0)
						continue;

					var tempBool = false;
					var tempInt = 0;

					if (Options.TryParse(opt, "Check", ref tempBool, StringComparison.OrdinalIgnoreCase, true, true))
						lvo.ischecked = tempBool;
					else if (opt.Equals("StartChecked", StringComparison.OrdinalIgnoreCase))
						lvo.ischecked = true;
					else if (opt.Equals("Checked", StringComparison.OrdinalIgnoreCase))
						lvo.ischecked = true;
					else if (Options.TryParse(opt, "Focus", ref tempBool, StringComparison.OrdinalIgnoreCase, true, true))
						lvo.focused = tempBool;
					else if (Options.TryParse(opt, "Select", ref tempBool, StringComparison.OrdinalIgnoreCase, true, true))
						lvo.select = tempBool;
					else if (Options.TryParse(opt, "Col", ref tempInt))
						lvo.colstart = tempInt - 1;
					else if (Options.TryParse(opt, "Icon", ref lvo.icon)) { }
					else if (opt.Equals("Vis", StringComparison.OrdinalIgnoreCase))
						lvo.vis = true;
				}

				return lvo;
			}

			private static void KeysharpListView_EnsureSubItems(KeysharpListView.ListViewItem item, int count)
			{
				while (item.SubItems.Count < count)
					item.SubItems.Add(new KeysharpListView.ListViewItem.ListViewSubItem());
			}

			private static string KeysharpListView_GetCellText(KeysharpListView.ListViewItem item, int columnIndex)
			{
				if (columnIndex == 0)
					return string.IsNullOrEmpty(item.Text) && item.SubItems.Count > 0 ? item.SubItems[0].Text : item.Text;

				return columnIndex < item.SubItems.Count ? item.SubItems[columnIndex].Text : "";
			}

			private static void KeysharpListView_SetCellText(KeysharpListView.ListViewItem item, int columnIndex, string value)
			{
				KeysharpListView_EnsureSubItems(item, columnIndex + 1);
				if (columnIndex == 0)
					item.Text = value ?? "";

				item.SubItems[columnIndex].Text = value ?? "";
			}

			public object Move(object x = null, object y = null, object width = null, object height = null)
			{
				if (_control == null)
					return DefaultObject;

				var location = _control.GetLocation();
				var size = _control.GetSize();
				var nx = x is null ? location.X : x.Ai();
				var ny = y is null ? location.Y : y.Ai();
				var nw = width is null ? size.Width : width.Ai();
				var nh = height is null ? size.Height : height.Ai();
				_control.SetLocation(new Point(nx, ny));
				_control.SetSize(new Size(nw, nh));
				return DefaultObject;
			}

			public object SetCue(object newText, object showWhenFocused = null)
			{
				string txt = newText.ToString();
				int showOnFocus = ForceBool(showWhenFocused ?? false) ? 1 : 0;

				if (_control is TextBox tb)
				{
					tb.PlaceholderText = txt;
					return DefaultObject;
				}

				return Errors.ValueErrorOccurred($"Only Edit controls implement this method.");
			}

			public object OnCommand(object notifyCode, object callback, object addRemove = null) => DefaultObject;
			public object OnEvent(object eventName, object callback, object addRemove = null)
			{
				var e = eventName.As().ToLower();
				var h = callback;
				var i = addRemove.Al(1);

				if (gui == null || !gui.TryGetTarget(out var g))
					return DefaultErrorObject;

				var del = Functions.GetFuncObj(h, g.form.eventObj, true);

				if (del != null)
				{
					if (e == "change")
					{
						if (changeHandlers == null)
							changeHandlers = [];

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
						doubleClickHandlers.ModifyEventHandlers(del, i);
					}
					else if (e == "focus")
					{
						if (focusHandlers == null)
							focusHandlers = [];

						focusHandlers.ModifyEventHandlers(del, i);
					}
					else if (e == "losefocus")
					{
						if (lostFocusHandlers == null)
							lostFocusHandlers = [];

						lostFocusHandlers.ModifyEventHandlers(del, i);
					}
					else if (e == "colclick")
					{
						if (_control is KeysharpListView lv)
						{
							if (columnClickHandlers == null)
								columnClickHandlers = [];

							columnClickHandlers.ModifyEventHandlers(del, i);
						}
					}
					else if (e == "itemcheck")
					{
						if (_control is KeysharpTreeView || _control is KeysharpListView)
						{
							if (itemCheckHandlers == null)
								itemCheckHandlers = [];

							itemCheckHandlers.ModifyEventHandlers(del, i);
						}
					}
					else if (e == "itemedit")
					{
						if (_control is KeysharpTreeView || _control is KeysharpListView)
						{
							if (itemEditHandlers == null)
								itemEditHandlers = [];

							itemEditHandlers.ModifyEventHandlers(del, i);
						}
					}
					else if (e == "itemexpand")
					{
						if (_control is KeysharpTreeView)
						{
							if (itemExpandHandlers == null)
								itemExpandHandlers = [];

							itemExpandHandlers.ModifyEventHandlers(del, i);
						}
					}
					else if (e == "itemfocus")
					{
						if (_control is KeysharpListView)
						{
							if (focusedItemChangedHandlers == null)
								focusedItemChangedHandlers = [];

							focusedItemChangedHandlers.ModifyEventHandlers(del, i);
						}
					}
					else if (e == "itemselect")
					{
						if (_control is KeysharpTreeView || _control is KeysharpListView)
						{
							if (selectedItemChangedHandlers == null)
								selectedItemChangedHandlers = [];

							selectedItemChangedHandlers.ModifyEventHandlers(del, i);
						}
					}
					else if (e == "contextmenu")
					{
						if (contextMenuChangedHandlers == null)
							contextMenuChangedHandlers = [];

						if (!(_control is KeysharpTextBox) && !(_control is KeysharpPasswordBox) && !(_control is KeysharpMonthCalendar))
							contextMenuChangedHandlers.ModifyEventHandlers(del, i);
					}
				}

				return DefaultObject;
			}
			public object OnNotify(object notifyCode, object callback, object addRemove = null) => DefaultObject;
			public object Opt(object options)
			{
				if (gui == null || !gui.TryGetTarget(out var g))
					return DefaultErrorObject;

				var opts = g.ParseOpt(typename, _control.Text, options.As());

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
					else if (opts.c.Value != _control.ForeColor)
						_control.ForeColor = opts.c.Value;
				}

				if (_control is KeysharpButton)
				{
					//if (opts.btndef.HasValue)
					//	g.form.AcceptButton = opts.btndef == true ? (IButtonControl)_control : null;
				}
				else if (_control is KeysharpListBox lb)
				{
					if (opts.vscroll.HasValue)
						lb.ScrollAlwaysVisible = opts.vscroll.Value;

					if (opts.hscrollamt != int.MinValue)
						lb.HorizontalScrollbar = true;

					if (opts.hscrollamt > 0)
						lb.HorizontalExtent = opts.hscrollamt;

					if (opts.sort.HasValue)
						lb.Sorted = opts.sort.Value;
				}
				else if (_control is KeysharpComboBox cb)
				{
					if (opts.sort.IsTrue())
					{
						cb.Sorted = true;

						if (cb.DropDownStyle != ComboBoxStyle.DropDownList)
						{
							cb.AutoComplete = true;
						}
					}
					else if (opts.sort.IsFalse())
					{
						cb.Sorted = false;

						if (cb.DropDownStyle != ComboBoxStyle.DropDownList)
						{
							cb.AutoComplete = false;
						}
					}

					if (typename != Keyword_DropDownList && opts.cmbsimple.HasValue)
					{
						cb.DropDownStyle = opts.cmbsimple.IsTrue() ? ComboBoxStyle.Simple : ComboBoxStyle.DropDown;
					}
				}
				else if (_control is KeysharpTextBox txt)
				{
					txt.AcceptsTab = opts.wanttab ?? false;
					txt.AcceptsReturn = opts.wantreturn ?? false;

					if (opts.wantctrla.IsFalse())
					{
						//txt.PreviewKeyDown += Core.Gui.SuppressCtrlAPreviewKeyDown;
						txt.KeyDown += Core.Gui.SuppressCtrlAKeyDown;
					}
					else if (opts.wantctrla.IsTrue())
					{
						//txt.PreviewKeyDown -= Core.Gui.SuppressCtrlAPreviewKeyDown;
						txt.KeyDown -= Core.Gui.SuppressCtrlAKeyDown;
					}

					if (opts.limit != int.MinValue)
						txt.MaxLength = opts.limit;

					txt.IsNumeric = opts.number;

					if (opts.lowercase.IsTrue())
						txt.CharacterCasing = CharacterCasing.Lower;
					else if (opts.uppercase.IsTrue())
						txt.CharacterCasing = CharacterCasing.Upper;
					else
						txt.CharacterCasing = CharacterCasing.Normal;
				}
				else if (_control is KeysharpPasswordBox ptxt)
				{
					ptxt.AcceptsTab = opts.wanttab ?? false;
					ptxt.AcceptsReturn = opts.wantreturn ?? false;

					if (opts.wantctrla.IsFalse())
					{
						ptxt.KeyDown += Core.Gui.SuppressCtrlAKeyDown;
					}
					else if (opts.wantctrla.IsTrue())
					{
						ptxt.KeyDown -= Core.Gui.SuppressCtrlAKeyDown;
					}

					if (opts.limit != int.MinValue)
						ptxt.MaxLength = opts.limit;

					ptxt.IsNumeric = opts.number;

					if (opts.lowercase.IsTrue())
						ptxt.CharacterCasing = CharacterCasing.Lower;
					else if (opts.uppercase.IsTrue())
						ptxt.CharacterCasing = CharacterCasing.Upper;
					else
						ptxt.CharacterCasing = CharacterCasing.Normal;

					if (opts.pwd)
					{
						if (opts.pwdch != "")
							ptxt.PasswordChar = opts.pwdch[0];
						else
							ptxt.UseSystemPasswordChar = true;
					}
				}
				else if (_control is KeysharpTextArea ttxt)
				{
					ttxt.AcceptsTab = opts.wanttab ?? true;
					ttxt.AcceptsReturn = opts.wantreturn ?? true;

					if (opts.wantctrla.IsFalse())
					{
						ttxt.KeyDown += Core.Gui.SuppressCtrlAKeyDown;
					}
					else if (opts.wantctrla.IsTrue())
					{
						ttxt.KeyDown -= Core.Gui.SuppressCtrlAKeyDown;
					}

					if (opts.vscroll.IsTrue() && opts.hscrollamt != int.MinValue)
						ttxt.ScrollBars = ScrollBars.Both;
					else if (opts.vscroll.IsTrue() || ttxt.Multiline)
						ttxt.ScrollBars = ScrollBars.Vertical;
					else if (opts.hscrollamt != int.MinValue)
						ttxt.ScrollBars = ScrollBars.Horizontal;

					if (opts.limit != int.MinValue)
						ttxt.MaxLength = opts.limit;

					ttxt.IsNumeric = opts.number;

					if (opts.lowercase.IsTrue())
						ttxt.CharacterCasing = CharacterCasing.Lower;
					else if (opts.uppercase.IsTrue())
						ttxt.CharacterCasing = CharacterCasing.Upper;
					else
						ttxt.CharacterCasing = CharacterCasing.Normal;

					if (opts.wordwrap.HasValue)
						ttxt.WordWrap = opts.wordwrap.IsTrue();
				}
				else if (_control is KeysharpRichEdit rtxt)
				{
					rtxt.AcceptsTab = opts.wanttab ?? false;

					if (opts.wantctrla.IsFalse())
					{
						//rtxt.PreviewKeyDown += Core.Gui.SuppressCtrlAPreviewKeyDown;
						rtxt.KeyDown += Core.Gui.SuppressCtrlAKeyDown;
					}
					else if (opts.wantctrla.IsTrue())
					{
						//rtxt.PreviewKeyDown -= Core.Gui.SuppressCtrlAPreviewKeyDown;
						rtxt.KeyDown -= Core.Gui.SuppressCtrlAKeyDown;
					}

					if (opts.vscroll.IsTrue() && opts.hscrollamt != int.MinValue)
						rtxt.ScrollBars = RichTextBoxScrollBars.Both;
					else if (opts.vscroll.IsTrue() || rtxt.Multiline)
						rtxt.ScrollBars = RichTextBoxScrollBars.Vertical;
					else if (opts.hscrollamt != int.MinValue)
						rtxt.ScrollBars = RichTextBoxScrollBars.Horizontal;

					if (opts.limit != int.MinValue)
						rtxt.MaxLength = opts.limit;

					rtxt.IsNumeric = opts.number;

					if (opts.lowercase.IsTrue())
						rtxt.CharacterCasing = CharacterCasing.Lower;
					else if (opts.uppercase.IsTrue())
						rtxt.CharacterCasing = CharacterCasing.Upper;
					else
						rtxt.CharacterCasing = CharacterCasing.Normal;
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
					if (opts.buttons.HasValue)
						tv.ShowPlusMinus = opts.buttons.Value;

					if (opts.rdonly.HasValue)
						tv.LabelEdit = !opts.rdonly.Value;

					if (opts.lines.HasValue)
						tv.ShowLines = opts.lines.Value;

					if (tv.LabelEdit)
					{
						if (opts.wantf2.HasValue && opts.wantf2.IsFalse())
							tv.KeyDown -= Core.Gui.Tv_Lv_KeyDown;
						else
						{
							tv.KeyDown -= Core.Gui.Tv_Lv_KeyDown;
							tv.KeyDown += Core.Gui.Tv_Lv_KeyDown;
						}
					}
					else
					{
						tv.KeyDown -= Core.Gui.Tv_Lv_KeyDown;
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
							lv.KeyDown -= Core.Gui.Tv_Lv_KeyDown;
						else
						{
							lv.KeyDown -= Core.Gui.Tv_Lv_KeyDown;
							lv.KeyDown += Core.Gui.Tv_Lv_KeyDown;
						}
					}
					else
					{
						lv.KeyDown -= Core.Gui.Tv_Lv_KeyDown;
					}

					if (opts.wantf2.HasValue)
						lv.AllowF2Edit = !opts.wantf2.IsFalse();

					if (opts.lvview.HasValue)
						lv.View = opts.lvview.Value;

					if ((opts.addlvstyle & 0x10) == 0x10)
						lv.AllowColumnReorder = true;
					else if ((opts.remlvstyle & 0x10) == 0x10)
						lv.AllowColumnReorder = false;

					if ((opts.addlvstyle & 0x20) == 0x20)
						lv.FullRowSelect = true;
					else if ((opts.remlvstyle & 0x20) == 0x20)
						lv.FullRowSelect = false;

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
					if (opts.halign.HasValue)
						nud.UpDownAlign = opts.halign.Value == GuiOptions.HorizontalAlignment.Left ? LeftRightAlignment.Left : LeftRightAlignment.Right;

					if (opts.nudinc.HasValue)
						nud.Increment = opts.nudinc.Value;

					if (opts.hex.HasValue)
						nud.Hexadecimal = opts.hex.Value;

					if (opts.nudlow.HasValue)
						nud.Minimum = opts.nudlow.Value;

					if (opts.nudhigh.HasValue)
						nud.Maximum = opts.nudhigh.Value;

					nud.ThousandsSeparator = (opts.addstyle & 0x80) != 0x80;
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

			public object Redraw()
			{
				_control.Invalidate();
				return DefaultObject;
			}
			public nint SetIcon(object fileName, object iconNumber = null, object partNumber = null)
			{
				if (_control is KeysharpStatusStrip ss)
				{
					var filename = fileName.As();
					var iconnumber = ImageHelper.PrepareIconNumber(iconNumber);
					var part = partNumber.Ai(1) - 1;
					(Bitmap, object) ret;

					if (part >= 0 && part < ss.Items.Count && (ret = ImageHelper.LoadImage(filename, 0, 0, iconnumber)).Item1 is Bitmap bmp)
					{
						ss.Items[part].Image = bmp;
						ss.UpdateItems();
						return 0;
					}
				}

				return 0;
			}
			public long SetImageList(object imageListID, object iconType = null)
			{
				var id = imageListID.Al();
				var type = iconType.Al(-1);
				var oldil = 0L;

				if (ImageLists.IL_Get(id) is ImageList il)
				{
					if (_control is KeysharpTreeView tv)
					{
						oldil = ImageLists.IL_GetId(tv.ImageList);
						tv.ImageList = il;
					}
					else if (_control is KeysharpListView lv)
					{
						oldil = ImageLists.IL_GetId(lv.ImageList);
						lv.ImageList = il;
					}
					else if (_control is KeysharpTabControl tc)
					{
						oldil = ImageLists.IL_GetId(tc.ImageList);
						tc.ImageList = il;
					}
				}

				return oldil;
			}
			public object SetParts(params object[] widths)
			{
				if (_control is KeysharpStatusStrip ss)
				{
					ss.Items.Clear();

					if (widths == null || widths.Length == 0)
					{
						ss.Items.Add(new KeysharpToolStripStatusLabel());
						ss.UpdateItems();
						return 0L;
					}

					for (var i = 0; i < widths.Length; i++)
					{
						var partWidth = widths[i]?.ParseInt();
						if (partWidth.HasValue)
						{
							ss.Items.Add(new KeysharpToolStripStatusLabel
							{
								AutoSize = false,
								Width = partWidth.Value
							});
						}
					}

					ss.Items.Add(new KeysharpToolStripStatusLabel { Spring = true, Width = -1 });
					ss.UpdateItems();
					return 0L;
				}

				return DefaultObject;
			}
			public object SetTabIcon(object tabIndex, object imageIndex)//New function since the original required SendMessage() to do this.
			{
				if (_control is KeysharpTabControl tc)
				{
					var tabindex = tabIndex.Ai();
					var imageindex = imageIndex.Ai();

					if (tabindex < tc.TabPages.Count && tc.ImageList != null && imageindex < tc.ImageList.Images.Count)
						tc.TabPages[tabindex].Image = tc.ImageList.Images[imageindex];
					else
						tc.TabPages[tabindex].Image = null;
				}

				return DefaultObject;
			}

			public bool SetText(object newText, object partNumber = null, object style = null)
			{
				if (_control is KeysharpStatusStrip ss)
				{
					var text = newText.As();
					var part = partNumber.Ai(1) - 1;
					var s = style.Al(-1);

					if (part >= 0 && part < ss.Items.Count)
					{
						ss.Items[part].Text = text;
						if (s >= 0)
							ss.Items[part].Style = (int)s;
						ss.UpdateItems();
						return true;
					}

					return false;
				}

				Text = newText;
				return true;
			}

			private void AssignParent(Forms.Control parent)
			{
				if (parent is not Forms.PixelLayout container)
					return;

				if (_control.Parent is Forms.Container oldContainer)
					oldContainer.Remove(_control);

				container.Add(_control, _control.Location);
			}

			internal object InvokeMessageHandlers(ref System.Windows.Forms.Message m) => 0L;

			private Point ConvertToClientPoint(int x, int y)
			{
				if (_control.ParentWindow is Window win)
				{
					var client = win.PointFromScreen(new PointF(x, y));
					return new Point((int)Math.Round(client.X), (int)Math.Round(client.Y));
				}

				return new Point(x, y);
			}

			internal void _control_Click(object sender, EventArgs e)
			{
				if (!eventHandlerActive)
					return;

				if (_control is KeysharpTreeView tv)
					_ = clickHandlers.InvokeEventHandlers(this, GetSelection());
				else if (_control is KeysharpListView lv)
				{
					if (lv.SelectedIndices.Count > 0)
						_ = clickHandlers.InvokeEventHandlers(this, lv.SelectedIndices[0] + 1L);
					else
						_ = clickHandlers.InvokeEventHandlers(this, 0L);
				}
				//else if (_control is KeysharpButton)
				//{
				//  //mousecount ^= 1;//Button click events get fired twice, because we have double click and standard click enabled, so filter the second click here.
				//  //if (mousecount > 0)
				//  _ = clickHandlers.InvokeEventHandlers(this, 0L);
				//}
				else
					_ = clickHandlers.InvokeEventHandlers(this, 0L);
			}

			internal void _control_KeyDown(object sender, KeyEventArgs e)
			{
				if ((eventHandlerActive && e.Key == Forms.Keys.RightApplication || (e.Key == Forms.Keys.F10 && ((e.Modifiers & Forms.Keys.Shift) == Forms.Keys.Shift))) && GetCursorPos(out POINT pt))
				{
					var clientPt = ConvertToClientPoint(pt.X, pt.Y);
					CallContextMenuChangeHandlers(true, clientPt.X, clientPt.Y);
				}
			}

			internal void _control_MouseDown(object sender, MouseEventArgs e)
			{
				if (_control is TextArea && e.Buttons == MouseButtons.Primary && !_control.HasFocus)
				{
					for (var parent = _control.Parent; parent != null; parent = parent.Parent)
					{
						if (parent is KeysharpTabControl)
						{
							Forms.Application.Instance.InvokeAsync(() => _control.Focus());
							break;
						}
					}
				}

				if (eventHandlerActive && e.Buttons == MouseButtons.Alternate)
				{
					var screenPt = _control.PointToScreen(e.Location);
					var clientPt = ConvertToClientPoint((int)Math.Round(screenPt.X), (int)Math.Round(screenPt.Y));
					CallContextMenuChangeHandlers(false, clientPt.X, clientPt.Y);
				}
			}

			internal void Tv_AfterExpand(object sender, TreeGridViewItemEventArgs e)
			{
				if (eventHandlerActive && _control is KeysharpTreeView)
					_ = (itemExpandHandlers?.InvokeEventHandlers(this, (e.Item as TreeNode)?.Handle.ToInt64() ?? 0L, e.Item.Expanded ? 1L : 0L));
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
					_ = (itemCheckHandlers?.InvokeEventHandlers(this, rowIndex + 1L, item.Checked ? 1L : 0L));
					return;
				}

				_ = (itemEditHandlers?.InvokeEventHandlers(this, rowIndex + 1L));
			}

			internal void Tv_AfterLabelEdit(object sender, GridViewCellEventArgs e)
			{
				if (!eventHandlerActive || _control is not KeysharpTreeView)
					return;

				if (e.Item is TreeNode node)
					_ = (itemEditHandlers?.InvokeEventHandlers(this, node.Handle.ToInt64()));
			}

			internal void Tv_AfterCheck(object sender, GridViewCellEventArgs e)
			{
				if (!eventHandlerActive || _control is not KeysharpTreeView tv || !tv.HasCheckBoxes)
					return;

				if (e.Column == tv.Columns.IndexOf(tv.CheckColumn) && e.Item is TreeNode node)
					_ = (itemCheckHandlers?.InvokeEventHandlers(this, node.Handle.ToInt64(), node.Checked ? 1L : 0L));
			}

			internal void Tv_CellClick(object sender, GridCellMouseEventArgs e)
			{
				if (!eventHandlerActive || _control is not KeysharpTreeView tv || !tv.HasCheckBoxes)
					return;

				if (e.Column != tv.Columns.IndexOf(tv.CheckColumn) || e.Item is not TreeNode node)
					return;

				node.Checked = !node.Checked;
				tv.CheckedBeginInvoke(new Action(tv.ReloadData), true, false);
				_ = (itemCheckHandlers?.InvokeEventHandlers(this, node.Handle.ToInt64(), node.Checked ? 1L : 0L));
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
				{
					return;
				}

				var rowIndex = e.Row;
				if (rowIndex < 0)
					rowIndex = lv.Items.IndexOf(item);
				if (rowIndex < 0)
					return;

				var grid = (Eto.Forms.GridView)lv;
				var checkColumnIndex = lv.HasCheckBoxes ? grid.Columns.IndexOf(lv.CheckColumn) : -1;
				if (checkColumnIndex >= 0 && e.Column == checkColumnIndex)
				{
					listViewCheckClickActive = true;
					item.Checked = !item.Checked;
					lv.RefreshDataStore();
					_ = (itemCheckHandlers?.InvokeEventHandlers(this, rowIndex + 1L, item.Checked ? 1L : 0L));
					_ = Eto.Forms.Application.Instance.InvokeAsync(() => listViewCheckClickActive = false);

					if (lv.MultiSelect)
						grid.SelectRow(rowIndex);
					else
						grid.SelectedRow = rowIndex;

					lv.FocusedItem = item;
					listViewFocusedRow = rowIndex;
					Lv_SelectedRowsChanged(sender, EventArgs.Empty);
					return;
				}

				if (lv.MultiSelect)
					grid.SelectRow(rowIndex);
				else
					grid.SelectedRow = rowIndex;

				lv.FocusedItem = item;
				listViewFocusedRow = rowIndex;
				Lv_SelectedRowsChanged(sender, EventArgs.Empty);
			}

			internal void Lv_SelectedRowsChanged(object sender, EventArgs e)
			{
				if (!eventHandlerActive || _control is not KeysharpListView lv)
					return;

				var prevFocusedRow = listViewFocusedRow;
				var current = new HashSet<int>();
				var grid = (Eto.Forms.GridView)lv;
				foreach (var index in grid.SelectedRows)
					current.Add(index + 1);
				if (current.Count == 0 && grid.SelectedRow >= 0)
					current.Add(grid.SelectedRow + 1);

				var previous = listViewSelectedRows ?? [];

				lv.SelectedItems.Clear();
				lv.SelectedIndices.Clear();
				for (var i = 0; i < lv.Items.Count; i++)
				{
					var isSelected = current.Contains(i + 1);
					lv.Items[i].Selected = isSelected;
					if (isSelected)
					{
						lv.SelectedItems.Add(lv.Items[i]);
						lv.SelectedIndices.Add(i);
					}
				}

				if (selectedItemChangedHandlers != null)
				{
					foreach (var row in current)
					{
						if (!previous.Contains(row))
							_ = selectedItemChangedHandlers.InvokeEventHandlers(this, (long)row, 1L);
					}

					foreach (var row in previous)
					{
						if (!current.Contains(row))
							_ = selectedItemChangedHandlers.InvokeEventHandlers(this, (long)row, 0L);
					}
				}

				listViewSelectedRows = current;

				var preferred = grid.SelectedRow >= 0 && current.Contains(grid.SelectedRow + 1)
					? grid.SelectedRow
					: current.Count > 0 ? current.Max() - 1 : -1;

				if (preferred >= 0 && preferred < lv.Items.Count)
				{
					lv.FocusedItem = lv.Items[preferred];
					listViewFocusedRow = preferred;
				}
				else
				{
					lv.FocusedItem = null;
					listViewFocusedRow = -1;
				}

				if (focusedItemChangedHandlers != null && listViewFocusedRow >= 0 && listViewFocusedRow != prevFocusedRow)
					_ = focusedItemChangedHandlers.InvokeEventHandlers(this, (long)(listViewFocusedRow + 1));
			}

			internal void Lv_ColumnClick(int columnIndex)
			{
				if (eventHandlerActive && _control is KeysharpListView)
					_ = (columnClickHandlers?.InvokeEventHandlers(this, columnIndex + 1L));
			}

			internal void Lv_KeyDownEdit(object sender, KeyEventArgs e)
			{
				if (_control is not KeysharpListView lv)
					return;

				if (!lv.LabelEdit || !lv.AllowF2Edit)
					return;

				if (e.Key != Forms.Keys.F2)
					return;

				if (lv.SelectedItems.Count > 0)
					lv.SelectedItems[0].BeginEdit();
				else if (lv.FocusedItem != null)
					lv.FocusedItem.BeginEdit();
				else if (lv.Items.Count > 0)
					lv.Items[0].BeginEdit();

				e.Handled = true;
			}

			internal void Lv_MouseDoubleClickEdit(object sender, MouseEventArgs e)
			{
				if (_control is not KeysharpListView lv)
					return;

				if (!lv.LabelEdit)
					return;

				if (lv.SelectedItems.Count > 0)
					lv.SelectedItems[0].BeginEdit();
				else if (lv.FocusedItem != null)
					lv.FocusedItem.BeginEdit();
			}

			internal void Tv_AfterSelect(object sender, EventArgs e)
			{
				if (eventHandlerActive && _control is KeysharpTreeView tv)
					_ = (selectedItemChangedHandlers?.InvokeEventHandlers(this, (tv.SelectedItem as TreeNode)?.Handle.ToInt64() ?? 0L));
			}

			internal void Txt_TextChanged(object sender, EventArgs e)
			{
				if (eventHandlerActive && (_control is KeysharpTextBox || _control is KeysharpPasswordBox || _control is KeysharpTextArea))
					_ = (changeHandlers?.InvokeEventHandlers(this, 0L));
			}
		}
	}
}
#endif
