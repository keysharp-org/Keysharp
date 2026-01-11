namespace Keysharp.Core
{
	/// <summary>
	/// Provides an interface to create and modify a menu or menu bar, add and modify menu items, and retrieve information about the menu or menu bar.
	/// Menu objects are used to define, modify and display popup menus. <see cref="Menu()"/>, <see cref="MenuFromHandle"/><br/>
	/// and <see cref="A_TrayMenu"/> return an object of this type.
	/// </summary>
	public class Menu : KeysharpObject
	{
		/// <summary>
		/// The default item in the menu.
		/// </summary>
		internal ToolStripItem defaultItem;

		/// <summary>
		/// A variable needed to assign <see cref="Handle"/> to once in the constructor
		/// to ensure the underlying handle is created. Unused otherwise.
		/// </summary>
		protected long dummyHandle;

		/// <summary>
		/// Click handlers for all menu items within this menu.
		/// Each item can have more than one click handler.
		/// </summary>
		private readonly Dictionary<ToolStripItem, List<IFuncObj>> clickHandlers = [];

		/// <summary>
		/// How many times the tray icon must be clicked to select its default menu item.
		/// </summary>
		public long ClickCount { get; set; } = 2;

		/// <summary>
		/// The default menu item to click when the tray icon is double clicked.
		/// </summary>
		public string Default
		{
			get => defaultItem != null ? defaultItem.Text : "";

			set
			{
				if (GetMenuItem(value) is ToolStripMenuItem item)
				{
					var allitems = GetMenu().GetItems();
					defaultItem = item;

					foreach (var defitem in allitems)
#if WINDOWS
						defitem.Font = defitem == item
									   ? new Font(item.Font, item.Font.Style | FontStyle.Bold)
									   : new Font(item.Font, item.Font.Style & ~FontStyle.Bold);
#else
						try 
						{
							defitem.Font = defitem == item
									   ? new Font(item.Font.Family.Name, item.Font.Size, item.Font.FontStyle | FontStyle.Bold)
									   : new Font(item.Font.Family.Name, item.Font.Size, item.Font.FontStyle & ~FontStyle.Bold);
						} catch {}
#endif
				}
				else
					defaultItem = null;
			}
		}

		/// <summary>
		/// The HWND of the menu.
		/// </summary>
		public long Handle => GetMenu().Handle.ToInt64();

		/// <summary>
		/// The number of sub items contained in the menu.
		/// </summary>
		public long MenuItemCount => GetMenu().Items.Count;

		/// <summary>
		/// The <see cref="ContextMenuStrip"/> that holds the menu items.
		/// </summary>
		internal ContextMenuStrip MenuItem { get; set; }

		public Menu(params object[] args) : base(args) { }

		/// <summary>
		/// Initializes a new instance of the <see cref="Menu"/> class.
		/// </summary>
		/// <param name="strip">Optional existing <see cref="ContextMenuStrip"/>. Default: false.</param>
		public override object __New(params object[] args)
		{
			MenuItem = (args.Length > 0 ? (ContextMenuStrip)args[0] : null) ?? new ContextMenuStrip();
			//GetMenu().ImageScalingSize = new System.Drawing.Size(28, 28);//Don't set scaling, it makes the checked icons look funny.
			var newCount = Interlocked.Increment(ref Script.TheScript.GuiData.menuCount);
			GetMenu().Name = $"Menu_{newCount}";
			dummyHandle = Handle;//Must access the handle once to force creation.
			return DefaultObject;
		}

		/// <summary>
		/// Adds or modifies a menu item.<br/>
		/// This is a multipurpose method that adds a menu item, updates one with a new submenu or callback,<br/>
		/// or converts one from a normal item into a submenu (or vice versa).<br/>
		/// If MenuItemName does not yet exist, it will be added to the menu.<br/>
		/// Otherwise, MenuItemName is updated with the newly specified CallbackOrSubmenu and/or Options.<br/>
		/// To add a menu separator line, omit all three parameters.
		/// </summary>
		/// <param name="menuItemName">The text to display on the menu item, or the position of an existing item to modify.</param>
		/// <param name="callbackOrSubmenu">The function to call as a new thread when the menu item is selected,<br/>
		/// or a reference to a Menu object to use as a submenu.<br/>
		/// This parameter is required when creating a new item, but optional when updating the options<br/>
		/// of an existing item.
		/// </param>
		/// <param name="options">If blank or omitted, it defaults to no options.<br/>
		/// Otherwise, specify one or more options from the list below (not case-sensitive).<br/>
		/// Separate each option from the next with a space or tab.<br/>
		/// To remove an option, precede it with a minus sign.<br/>
		/// To add an option, a plus sign is permitted but not required.<br/>
		///     Pn: Specify for n the menu item's thread priority, e.g. P1.<br/>
		///         If this option is omitted when adding a menu item, the priority will be 0,<br/>
		///         which is the standard default. If omitted when updating a menu item, the item's<br/>
		///         priority will not be changed.Use a decimal (not hexadecimal) number as the priority.<br/>
		///     Radio: If the item is checked, a bullet point is used instead of a check mark.<br/>
		///     Right: The item is right-justified within the menu bar.<br/>
		///         This only applies to menu bars, not popup menus or submenus.<br/>
		///     Break: The item begins a new column in a popup menu.<br/>
		///     BarBreak: As above, but with a dividing line between columns.<br/>
		/// To change an existing item's options without affecting its callback or submenu, simply omit the CallbackOrSubmenu parameter.
		/// </param>
		/// <returns>Null if a separator was added, else the newly added <see cref="ToolStripMenuItem"/>.</returns>
		public object Add(object menuItemName = null, object callbackOrSubmenu = null, object options = null)
		{
			if (menuItemName == null && callbackOrSubmenu == null && options == null)
			{
				_ = GetMenu().Items.Add(new ToolStripSeparator());
				return null;
			}

			return AddOrInsert("", menuItemName.As(), callbackOrSubmenu, options.As());
		}

		/// <summary>
		/// Adds the standard tray menu items after any existing items.<br/>
		/// Any standard items already in the menu are not duplicated, but any missing items are added.
		/// </summary>
		public object AddStandard()
		{
			var menu = GetMenu();
			var emptyfunc = (params object[] args) => "";
			var script = Script.TheScript;
			var openfunc = (params object[] args) =>
			{
				var mainWindow = script.mainWindow;

				if (mainWindow != null && A_AllowMainWindow.Ab())
				{
					mainWindow.AllowShowDisplay = true;
					mainWindow.Show();
					mainWindow.BringToFront();
					mainWindow.WindowState = mainWindow.lastWindowState;
				}

				return DefaultObject;
			};
			var reloadfunc = (params object[] args) =>
			{
				_ = Flow.Reload();
				return DefaultObject;
			};
			var suspend = (params object[] args) =>
			{
				Script.SuspendHotkeys();
				return DefaultObject;
			};
			var exitfunc = (params object[] args) =>
			{
				_ = Flow.ExitAppInternal(Flow.ExitReasons.Menu, null, true);
				return DefaultObject;
			};
			//Won't be a gui target, so won't be marked as IsGui internally, but it's ok because it's only ever called on the gui thread in response to gui events.
			script.openMenuItem = (ToolStripMenuItem)Add("&Open", new FuncObj(openfunc.Method, openfunc.Target));

			if (!A_AllowMainWindow.Ab())
				script.openMenuItem.Visible = false;

			//Need to fill in the event handlers for help and window spy when the proper functionality is implemented.//TODO
			//_ = Add("&Help", new FuncObj(emptyfunc.Method, emptyfunc.Target));
			if (menu.Items.Cast<ToolStripItem>().Any(tsi => tsi.Visible))
				_ = menu.Items.Add(new ToolStripSeparator());

			//_ = Add("&Window Spy", new FuncObj(emptyfunc.Method, emptyfunc.Target));
			_ = Add("&Reload Script", new FuncObj(reloadfunc.Method, reloadfunc.Target));

			if (!A_IsCompiled)
			{
				var editfunc = (params object[] args) =>
				{
					_ = Debug.Edit();
					return DefaultObject;
				};
				_ = Add("&Edit Script", new FuncObj(editfunc.Method, editfunc.Target));
			}

			_ = menu.Items.Add(new ToolStripSeparator());
			script.suspendMenuItem = (ToolStripMenuItem)Add("&Suspend Hotkeys", new FuncObj(suspend.Method, suspend.Target));
			_ = Add("E&xit", new FuncObj(exitfunc.Method, exitfunc.Target));
			return DefaultObject;
		}

		/// <summary>
		/// Adds a visible checkmark in the menu next to a menu item (if there isn't one already).
		/// </summary>
		/// <param name="menuItemName">The name or position of a menu item.</param>
		/// <returns>The new check state as a boolean.</returns>
		public bool Check(object menuItemName) => Check(menuItemName.As(), eCheckToggle.Check);

		/// <summary>
		/// Deletes one or all menu items.
		/// </summary>
		/// <param name="menuItemName">If omitted, all menu items are deleted from the menu,<br/>
		/// leaving the menu empty. Otherwise, specify the name or position of a menu item.
		/// </param>
		public object Delete(object menuItemName)
		{
			var s = menuItemName.As();

			if (s?.Length == 0)
			{
				GetMenu().Items.Clear();
				clickHandlers.Clear();
			}
			else if (GetMenuItem(s) is ToolStripItem item)
			{
				if (item == defaultItem)
					defaultItem = null;

				if (item.GetCurrentParent() is ToolStripDropDownMenu tsddm)
					tsddm.Items.Remove(item);
				else
					GetMenu().Items.Remove(item);

				GetMenu().Refresh();
				_ = clickHandlers.Remove(item);
			}

			return DefaultObject;
		}

		/// <summary>
		/// Grays out a menu item to indicate that the user cannot select it.
		/// </summary>
		/// <param name="menuItemName">The name or position of a menu item.</param>
		/// <returns>The new enabled state as a boolean.</returns>
		public bool Disable(object menuItemName) => Enable(menuItemName.As(), eCheckToggle.Uncheck);

		/// <summary>
		/// Allows the user to once again select a menu item if it was previously disabled (grayed out).
		/// </summary>
		/// <param name="menuItemName">The name or position of a menu item.</param>
		/// <returns>The new enabled state as a boolean.</returns>
		public bool Enable(object menuItemName) => Enable(menuItemName.As(), eCheckToggle.Check);

		/// <summary>
		/// Hides a menu item.
		/// </summary>
		/// <param name="menuItemName">The name or position of a menu item.</param>
		/// <returns>The new visibility state as a boolean.</returns>
		public bool HideItem(object menuItemName) => MakeVisible(menuItemName.As(), eCheckToggle.Uncheck);

		/// <summary>
		/// Inserts a new item before the specified item.<br/>
		/// To insert a menu separator line before an existing custom menu item, omit all parameters except MenuItemName.<br/>
		/// To add a menu separator line at the bottom of the menu, omit all parameters.
		/// </summary>
		/// <param name="menuItemName">If blank or omitted, itemToInsert will be added at the bottom of the menu.<br/>
		/// Otherwise, specify the name or position of an existing custom menu item before which itemToInsert should be inserted.
		/// </param>
		/// <param name="itemToInsert">The name of a new menu item to insert before MenuItemName.</param>
		/// <param name="callbackOrSubmenu">See the <see cref="Add"/> method's callbackOrSubmenu parameter.</param>
		/// <param name="options">See the <see cref="Add"/> method's options parameter.</param>
		/// <returns>The newly create <see cref="ToolStripMenuItem"/>.</returns>
		public object Insert(object menuItemName = null, object itemToInsert = null, object callbackOrSubmenu = null, object options = null) => AddOrInsert(menuItemName.As(), itemToInsert.As(), callbackOrSubmenu, options.As());

		/// <summary>
		/// Gets the name of a menu item.
		/// </summary>
		/// <param name="menuItemName">The name or position of a menu item.</param>
		/// <returns>The name of the retrieved menu item if found, else empty string.</returns>
		public string MenuItemName(object menuItemName) => GetMenuItem(menuItemName.As()) is ToolStripMenuItem tsmi ? tsmi.Name : "";

		/// <summary>
		/// Renames a menu item.
		/// </summary>
		/// <param name="menuItemName">The name or position of a menu item.</param>
		/// <param name="newName">If blank or omitted, menuItemName will be converted into a separator line.<br/>
		/// Otherwise, specify the new name.
		/// </param>
		public object Rename(object menuItemName, object newName = null)
		{
			var name = menuItemName.As();
			var newname = newName.As("-");
			var item = GetMenuItem(name);

			if (item is ToolStripSeparator tss)
			{
				var index = (int)GetIndex(tss);
				var newItem = new ToolStripMenuItem(newname);
				newItem.Name = newItem.Text = newname;
				GetMenu().Items.RemoveAt(index);
				GetMenu().Items.Insert(index, newItem);
			}

			if (item is ToolStripItem tsi)
				tsi.Name = tsi.Text = newname;

			return DefaultObject;
		}

		/// <summary>
		/// Changes the background color of the menu.
		/// </summary>
		/// <param name="colorValue">If blank or omitted, it defaults to the word Default, which restores the default<br/>
		/// color of the menu. Otherwise, specify one of the 16 primary HTML color names,<br/>
		/// a hexadecimal RGB color string (the 0x prefix is optional), or a pure numeric RGB color value.
		/// </param>
		/// <param name="applyToSubmenus">If omitted, it defaults to true.<br/>
		/// If true, the color will be applied to all of the menu's submenus.<br/>
		/// If false, the color will be applied to the menu only.
		/// </param>
		public object SetColor(object colorValue = null, object applyToSubmenus = null) => HandleColor(GetMenu(), colorValue.As(), applyToSubmenus.Ab(true), true);

		/// <summary>
		/// Changes the foreground (text) color of the menu.
		/// </summary>
		/// <param name="colorValue">If blank or omitted, it defaults to the word Default, which restores the default<br/>
		/// color of the menu. Otherwise, specify one of the 16 primary HTML color names,<br/>
		/// a hexadecimal RGB color string (the 0x prefix is optional), or a pure numeric RGB color value.
		/// </param>
		/// <param name="applyToSubmenus">If omitted, it defaults to true.<br/>
		/// If true, the color will be applied to all of the menu's submenus.<br/>
		/// If false, the color will be applied to the menu only.
		/// </param>
		public object SetForeColor(object colorValue = null, object applyToSubmenus = null) => HandleColor(GetMenu(), colorValue.As(), applyToSubmenus.Ab(true), false);

		/// <summary>
		/// Sets the icon to be displayed next to a menu item.
		/// </summary>
		/// <param name="menuItemName">The name or position of a menu item.</param>
		/// <param name="fileName">The path to an icon or image file, or a bitmap or icon handle such as "HICON:" handle.<br/>
		/// Specify an empty string or "*" to remove the item's current icon.
		/// </param>
		/// <param name="iconNumber">If omitted, it defaults to 1 (the first icon group).<br/>
		/// Otherwise, specify the number of the icon group to be used in the file.<br/>
		/// If negative, its absolute value is assumed to be the resource ID of an icon within an executable file.
		/// </param>
		/// <param name="iconWidth">If omitted, it defaults to the width of a small icon recommended by<br/>
		/// the OS (usually 16 pixels). If 0, the original width is used.<br/>
		/// Otherwise, specify the desired width of the icon, in pixels.<br/>
		/// If the icon group indicated by IconNumber contains multiple icon sizes, the closest match is used<br/>
		/// and the icon is scaled to the specified size.
		/// </param>
		public object SetIcon(object menuItemName, object fileName, object iconNumber = null, object iconWidth = null)
		{
			var name = menuItemName.As();
			var filename = fileName.As();
			var iconnumber = ImageHelper.PrepareIconNumber(iconNumber);
			var width = iconWidth.Ai();

			if (GetMenuItem(name) is ToolStripItem tsmi)
			{
				if (ImageHelper.LoadImage(filename, width, 0, iconnumber).Item1 is Bitmap bmp)
					tsmi.Image = bmp;
			}

			return DefaultObject;
		}

		/// <summary>
		/// Displays the menu.
		/// </summary>
		/// <param name="x,y">If omitted, the menu will be shown near the mouse cursor.<Br/>
		/// Otherwise, specify the X and Y coordinates at which to display the upper left corner of the menu.<br/>
		/// The coordinates are relative to the active window's client area unless overridden by using <see cref="CoordMode"/> or <see cref="A_CoordModeMenu"/>.
		/// </param>
		public object Show(object x = null, object y = null)
		{
			_ = GetCursorPos(out POINT def);
			var _x = x.Ai();
			var _y = y.Ai();
			if (x != null || y != null) CoordToScreen(ref _x, ref _y, Core.CoordMode.Menu);
			if (x == null) _x = def.X;
			if (y == null) _y = def.Y;
			var pt = new Point(_x, _y);					

			MenuItem.Show(pt);
			return DefaultObject;
		}

		/// <summary>
		/// Shows a menu item.
		/// </summary>
		/// <param name="menuItemName">The name or position of a menu item.</param>
		/// <returns>The new visibility state as a boolean.</returns>
		public bool ShowItem(object menuItemName) => MakeVisible(menuItemName.As(), eCheckToggle.Check);

		/// <summary>
		/// Adds a checkmark if there wasn't one; otherwise, removes it.
		/// </summary>
		/// <param name="menuItemName">The name or position of a menu item.</param>
		/// <returns>The new check state as a boolean.</returns>
		public bool ToggleCheck(object menuItemName) => Check(menuItemName.As(), eCheckToggle.Toggle);

		/// <summary>
		/// Disables a menu item if it was previously enabled; otherwise, enables it.
		/// </summary>
		/// <param name="menuItemName">The name or position of a menu item.</param>
		/// <returns>The new enabled state as a boolean.</returns>
		public bool ToggleEnable(object menuItemName) => Enable(menuItemName.As(), eCheckToggle.Toggle);

		/// <summary>
		/// Toggles the visibility of a menu item.
		/// </summary>
		/// <param name="menuItemName">The name or position of a menu item.</param>
		/// <returns>The new visibility state as a boolean.</returns>
		public bool ToggleItemVis(object menuItemName) => MakeVisible(menuItemName.As(), eCheckToggle.Toggle);

		/// <summary>
		/// Removes the checkmark (if there is one) from a menu item.
		/// </summary>
		/// <param name="menuItemName">The name or position of a menu item.</param>
		/// <returns>The new check state as a boolean.</returns>
		public bool UnCheck(object obj) => Check(obj.As(), eCheckToggle.Uncheck);

		internal void Tsmi_Click(object sender, EventArgs e)
		{
			if (sender is ToolStripMenuItem tsmi)
			{
				if (clickHandlers.TryGetValue(tsmi, out var handler))
				{
					var index = GetIndex(tsmi);
					_ = handler.InvokeEventHandlers(tsmi.Text, ++index, this);
				}
			}
		}

		protected internal virtual ToolStrip GetMenu() => MenuItem;

		protected static object HandleColor(ToolStrip menu, string name, bool submenus, bool backcolor)
		{
			if (Conversions.TryParseColor(name, out var color))
			{
				if (backcolor)
					menu.BackColor = color;
				else
					menu.ForeColor = color;

				if (submenus)
				{
					foreach (var item in menu.GetItems())
						if (backcolor)
							item.BackColor = color;
						else
							item.ForeColor = color;
				}
			}

			return DefaultObject;
		}

		protected virtual long GetIndex(ToolStripItem tsi) => tsi.GetCurrentParent() is ToolStripDropDownMenu tsddm ? tsddm.Items.IndexOf(tsi) : GetMenu().Items.IndexOf(tsi);

		protected virtual object GetMenuItem(string s)
		{
			if (s.EndsWith('&') && int.TryParse(s.Trim('&'), out var i) && i > 0)
			{
				if (GetMenu().Items[--i] is ToolStripItem tsmi)
					return tsmi;
			}
			else if (GetMenu().Items.Find(s, true).FirstOrDefault() is ToolStripItem tsmi)
				return tsmi;

			return DefaultErrorObject;
		}

		private object AddOrInsert(string insertbefore, string name, object funcorsub, string options)
		{
			ToolStripMenuItem item = null;

			if (!string.IsNullOrEmpty(insertbefore))
			{
				if (GetMenu().Items.Find(insertbefore, true).FirstOrDefault() is ToolStripMenuItem tsmiinsert)
				{
					var index = GetIndex(tsmiinsert);

					if (tsmiinsert.GetCurrentParent() is ToolStripDropDownMenu tsddm)
					{
						if (name?.Length == 0)
						{
							tsddm.Items.Insert((int)index, new ToolStripSeparator());
							return DefaultObject;
						}
						else
						{
							item = new ToolStripMenuItem(name);
							item.Click += Tsmi_Click;
							item.Name = name;
							tsddm.Items.Insert((int)index, item);
						}
					}
					else
					{
						item = new ToolStripMenuItem(name);
						item.Click += Tsmi_Click;
						item.Name = name;
						GetMenu().Items.Insert((int)index, item);
					}
				}
			}
			else
			{
				if (GetMenu().Items.Find(name, true).FirstOrDefault() is ToolStripMenuItem tsmi)
				{
					item = tsmi;
				}
				else if (GetMenu().Items.Add(name) is ToolStripMenuItem tsmi2)
				{
					tsmi2.Click += Tsmi_Click;
					tsmi2.Name = name;
					item = tsmi2;
				}
			}

			if (item != null)
			{
				if (string.IsNullOrEmpty(Default) && item.Text == "&Open")
				{
					Default = "&Open";
				}

				if (funcorsub is Menu mnu)
				{
					var fromMenuItems = mnu.GetMenu().Items;

					while (fromMenuItems.Count > 0)//Must use this because add range doesn't work.
					{
						var moveItem = fromMenuItems[0];
#if WINDOWS
						_ = item.DropDownItems.Add(moveItem);
#else
						//Windows automatically removes a menu item from one collection when it is added to another, but linux doesn't.
						//So it must be done manually here by moving the item between collections.
						fromMenuItems.RemoveAt(0);
						moveItem.ResetEtoItemRecursive();
						item.DropDownItems.Add(moveItem);
						moveItem.Owner = item.DropDown;
#endif
					}
#if !WINDOWS
					item.Owner?.SyncEtoItems();
#endif
				}
				else
					clickHandlers.GetOrAdd(item).ModifyEventHandlers(Functions.GetFuncObj(funcorsub, null, true), 1);

				foreach (Range r in options.AsSpan().SplitAny(Spaces))
				{
					var opt = options.AsSpan(r).Trim();

					if (opt.Length > 0)
					{
						var temp = 0;
						var tempbool = false;

						if (Options.TryParse(opt, "P", ref temp)) { }
						else if (Options.TryParse(opt, "Radio", ref tempbool, StringComparison.OrdinalIgnoreCase, true, true)) { }
						else if (Options.TryParse(opt, "Right", ref tempbool, StringComparison.OrdinalIgnoreCase, true, true))
						{
							item.TextAlign = tempbool ? ContentAlignment.MiddleRight : ContentAlignment.MiddleLeft;
						}
						else if (Options.TryParse(opt, "Break", ref tempbool, StringComparison.OrdinalIgnoreCase, true, true)) { }
						else if (Options.TryParse(opt, "BarBreak", ref tempbool, StringComparison.OrdinalIgnoreCase, true, true)) { }
					}
				}
			}

			return item != null ? item : "";
		}

		private bool Check(string s, eCheckToggle checktoggle)
		{
			if (GetMenuItem(s) is ToolStripMenuItem item)
			{
				if (checktoggle == eCheckToggle.Check)
					item.Checked = true;
				else if (checktoggle == eCheckToggle.Uncheck)
					item.Checked = false;
				else
					item.Checked = !item.Checked;

				return item.Checked;
			}

			return false;
		}

		private bool Enable(string s, eCheckToggle checktoggle)
		{
			if (GetMenuItem(s) is ToolStripItem item)
			{
				if (checktoggle == eCheckToggle.Check)
					item.Enabled = true;
				else if (checktoggle == eCheckToggle.Uncheck)
					item.Enabled = false;
				else
					item.Enabled = !item.Enabled;

				return item.Enabled;
			}

			return false;
		}

		private bool MakeVisible(string s, eCheckToggle vis)
		{
			if (GetMenuItem(s) is ToolStripMenuItem item)
			{
				if (vis == eCheckToggle.Toggle)
					item.Visible = !item.Visible;
				else if (vis == eCheckToggle.Check)
					item.Visible = true;
				else
					item.Visible = false;

				return item.Visible;
			}

			return false;
		}

		private void subMenuItem1_Click(object sender, EventArgs e)
		{
		}

		private enum eCheckToggle
		{
			Check,
			Uncheck,
			Toggle
		}
	}

	/// <summary>
	/// Derivation from <see cref="Menu"/> to implement toolbar/menubar functionality.
	/// </summary>
	public class MenuBar : Menu
	{
		/// <summary>
		/// The <see cref="MenuStrip"/> for the menubar.
		/// </summary>
		internal MenuStrip MenuStrip { get; } = new MenuStrip();

		/// <summary>
		/// Initializes a new instance of the <see cref="MenuBar"/> class.
		/// </summary>
		/// <param name="strip">The optional <see cref="ContextMenuStrip"/> to use for the menubar. Default: null.</param>
		public MenuBar(ContextMenuStrip strip = null)
			: base(strip)
		{
			MenuStrip.Dock = DockStyle.Top;
		}

		/// <summary>
		/// Gets the <see cref="MenuStrip"/>.
		/// </summary>
		/// <returns>A <see cref="ToolStrip"/></returns>
		protected internal override ToolStrip GetMenu()
		{
#if WINDOWS
			return MenuStrip;
#else
			return MenuStrip.ToolStrip;
#endif
		}

		/// <summary>
		/// Gets the index of the passed in <see cref="ToolStripItem"/> within <see cref="MenuStrip"/>.
		/// </summary>
		/// <param name="tsi">The <see cref="ToolStripItem"/> to search for.</param>
		/// <returns>The index if found, else -1.</returns>
		protected override long GetIndex(ToolStripItem tsi) => MenuStrip.Items.IndexOf(tsi);
	}
}