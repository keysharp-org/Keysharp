using Keysharp.Runtime;

namespace Keysharp.Builtins
{
	/// <summary>
	/// Provides an interface to create and modify a menu or menu bar, add and modify menu items, and retrieve information about the menu or menu bar.
	/// Menu objects are used to define, modify and display popup menus. <see cref="Menu()"/>, <see cref="MenuFromHandle"/><br/>
	/// and <see cref="A_TrayMenu"/> return an object of this type.
	/// </summary>
	public class Menu : KeysharpObject
	{
		/// <summary>
		/// The AHK v2.1 item options that have no direct WinForms/Eto equivalent and so must be reproduced by
		/// hand. AutoHotkey stores these as MFT_* bits on the native menu item; here they live on the item's Tag.
		/// </summary>
		internal sealed class MenuItemPresentation
		{
			internal bool BarBreak;
			internal bool Break;
			internal bool Radio;
			internal bool Right;
			internal bool Rtl;

			//Set on separators in a columned menu, which WinForms would otherwise stretch across the whole
			//window; 0 means "not in a columned menu". See KeysharpMenuRenderer.OnRenderSeparator.
			internal int ColumnWidth;

			//Both flavors begin a new column; BarBreak additionally draws a divider in front of it.
			internal bool StartsColumn => Break || BarBreak;
		}

		internal static MenuItemPresentation GetPresentation(ToolStripItem item) =>
			item.Tag as MenuItemPresentation ?? (MenuItemPresentation)(item.Tag = new MenuItemPresentation());

		//"Break" and "BarBreak" are mutually exclusive: a column starts either with a divider or without one.
		//Removing one leaves the other alone, matching AHK's per-flag MFT_MENUBREAK/MFT_MENUBARBREAK clearing.
		private static void SetColumnBreak(ToolStripItem item, bool adding, bool withBar)
		{
			var presentation = GetPresentation(item);

			if (adding)
				(presentation.BarBreak, presentation.Break) = (withBar, !withBar);
			else if (withBar)
				presentation.BarBreak = false;
			else
				presentation.Break = false;
		}

#if WINDOWS
		//Left margin reserved on a BarBreak column, with the divider drawn down the middle of it.
		private const int ColumnGap = 8;

		private sealed class KeysharpMenuRenderer : ToolStripProfessionalRenderer
		{
			//Win32 draws a bullet for MFT_RADIOCHECK items; ToolStrip only knows how to draw a tick.
			protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
			{
				if (e.Item.Tag is not MenuItemPresentation { Radio: true })
				{
					base.OnRenderItemCheck(e);
					return;
				}

				var bounds = e.ImageRectangle;
				var diameter = Math.Max(5, Math.Min(bounds.Width, bounds.Height) / 2);
				var oldSmoothing = e.Graphics.SmoothingMode;
				e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

				using (var brush = new SolidBrush(e.Item.Enabled ? SystemColors.MenuText : SystemColors.GrayText))
					e.Graphics.FillEllipse(brush, bounds.Left + ((bounds.Width - diameter) / 2),
										   bounds.Top + ((bounds.Height - diameter) / 2), diameter, diameter);

				e.Graphics.SmoothingMode = oldSmoothing;
			}

			//A separator belongs to one column, exactly as a native menu draws it. WinForms sizes separators to
			//the whole window regardless of the width given to them, so the drawing is clipped to the column.
			protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
			{
				if (e.Item.Tag is not MenuItemPresentation { ColumnWidth: > 0 } presentation || presentation.ColumnWidth >= e.Item.Width)
				{
					base.OnRenderSeparator(e);
					return;
				}

				var clip = e.Graphics.Clip;
				e.Graphics.SetClip(new Rectangle(0, 0, presentation.ColumnWidth, e.Item.Height));
				base.OnRenderSeparator(e);
				e.Graphics.Clip = clip;
			}

			//Draws the MFT_MENUBARBREAK divider. This runs on every repaint, so a menu with no BarBreak item
			//must cost no more than the scan itself.
			protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
			{
				base.OnRenderToolStripBackground(e);
				var first = true;

				foreach (ToolStripItem item in e.ToolStrip.Items)
				{
					if (!item.Available)
						continue;

					//The first visible item already starts the first column, so it never gets a divider.
					if (!first && item.Tag is MenuItemPresentation { BarBreak: true })
					{
						using var pen = new Pen(SystemColors.ControlDark);
						var x = Math.Max(0, item.Bounds.Left - (ColumnGap / 2));
						e.Graphics.DrawLine(pen, x, e.AffectedBounds.Top + 2, x, e.ToolStrip.ClientSize.Height - 3);
					}

					first = false;
				}
			}
		}

		private static readonly ToolStripRenderer menuRenderer = new KeysharpMenuRenderer();

		/// <summary>
		/// Reproduces MFT_MENUBREAK/MFT_MENUBARBREAK. ToolStrip has no native multi-column support, so the items
		/// are placed with a wrapping flow layout and every size is derived from their own preferred sizes.
		/// Runs when the menu is about to open, so it sees the final item list exactly once per display.
		/// Nothing here may be measured from the laid-out Bounds: a multi-column menu keeps the explicit size it
		/// is given, so feeding a measured result back in would grow the menu a little on every display.
		/// </summary>
		private static void ApplyColumns(ToolStripDropDown dropDown)
		{
			var flow = (FlowLayoutSettings)dropDown.LayoutSettings;
			var items = dropDown.Items.Cast<ToolStripItem>().Where(static item => item.Available).ToArray();

			//A drop-down already defaults to a single top-down column, so there is nothing to do unless this
			//menu wants columns or is still laid out in them from a previous display.
			if (!flow.WrapContents && !items.Skip(1).Any(static item => item.Tag is MenuItemPresentation { StartsColumn: true }))
				return;

			//Undo any pinning left by a previous display so the menu measures at its natural size again.
			foreach (var item in items)
			{
				flow.SetFlowBreak(item, false);
				item.AutoSize = true;
				item.Margin = new Padding(0, item.Margin.Top, item.Margin.Right, item.Margin.Bottom);

				if (item.Tag is MenuItemPresentation existing)
					existing.ColumnWidth = 0;
			}

			flow.WrapContents = false;
			dropDown.AutoSize = true;

			//Nothing visible left to arrange — every item was deleted or hidden since the last display, which is
			//only reachable here because the leftover WrapContents got us past the check above. The reset already
			//restored the plain single column, and the measuring below has no items to take a maximum over.
			if (items.Length == 0)
				return;

			//A ToolStripDropDownMenu stretches every item to the widest one in the menu and reports that width
			//as each item's preferred size, so the items cannot be measured individually. Their text can be, and
			//the surrounding chrome (check/image margin, padding, submenu arrows) is what the menu's own natural
			//width adds on top of the widest text.
			var texts = items.ToDictionary(item => item, item => TextRenderer.MeasureText(item.Text ?? "", item.Font ?? dropDown.Font).Width);
			var heights = items.ToDictionary(static item => item, static item => item.GetPreferredSize(Size.Empty).Height);
			var chrome = Math.Max(0, dropDown.GetPreferredSize(Size.Empty).Width - texts.Values.Max());
			var columns = new List<(List<ToolStripItem> Items, int Gap)> { ([], 0) };

			foreach (var item in items)
			{
				//The first visible item already starts the first column and so cannot begin another.
				if (columns[^1].Items.Count > 0 && item.Tag is MenuItemPresentation { StartsColumn: true } presentation)
					columns.Add(([], presentation.BarBreak ? ColumnGap : 0));

				columns[^1].Items.Add(item);
			}

			//Already restored to a plain single column above.
			if (columns.Count == 1)
				return;

			//Each column is sized to its own widest item, as a native menu does.
			var totalWidth = 0;
			var maxHeight = 0;

			foreach (var (columnItems, gap) in columns)
			{
				var width = chrome + columnItems.Max(item => texts[item]);
				var height = 0;

				foreach (var item in columnItems)
				{
					var margin = item.Margin;
					item.Margin = new Padding(gap, margin.Top, margin.Right, margin.Bottom);
					item.AutoSize = false;
					item.Size = new Size(width, heights[item]);
					height += item.Height + margin.Vertical;

					//WinForms ignores the width above for separators and stretches them across the window, so
					//the renderer has to clip them back to their own column.
					if (item is ToolStripSeparator)
						GetPresentation(item).ColumnWidth = width;
				}

				//Ends the column explicitly, so the wrap points never depend on the window's height.
				flow.SetFlowBreak(columnItems[^1], true);
				totalWidth += width + gap;
				maxHeight = Math.Max(maxHeight, height);
			}

			flow.WrapContents = true;
			dropDown.AutoSize = false;
			dropDown.Size = new Size(totalWidth + dropDown.Padding.Horizontal, maxHeight + dropDown.Padding.Vertical);
		}
#endif

		/// <summary>
		/// Gives a menu the Keysharp renderer and, for popups, arranges its columns just before it opens.
		/// Idempotent. No-op off Windows, where the native menus provide these features themselves
		/// (see UnixMenuPresentation).
		/// </summary>
		/// <param name="menu">The menu to initialize.</param>
		internal static void InitMenu(ToolStrip menu)
		{
#if WINDOWS
			if (menu == null)
				return;

			menu.Renderer = menuRenderer;

			//A drop-down that belongs to a menu item reports its owner's renderer rather than its own, so the
			//renderer cannot double as an "already initialized" marker here: a submenu of an initialized menu bar
			//already claims to have it and would never get its column handling. Detaching first is what keeps
			//repeated calls from stacking up handlers instead.
			if (menu is ToolStripDropDown dropDown)
			{
				dropDown.Opening -= DropDown_Opening;
				dropDown.Opening += DropDown_Opening;
			}
#endif
		}

#if WINDOWS
		private static void DropDown_Opening(object sender, CancelEventArgs e) => ApplyColumns((ToolStripDropDown)sender);
#endif

		/// <summary>
		/// The default item in the menu.
		/// </summary>
		internal ToolStripItem defaultItem;

		/// <summary>
		/// A variable needed to assign <see cref="Handle"/> to once in the constructor
		/// to ensure the underlying handle is created. Unused otherwise.
		/// </summary>
		protected long dummyHandle;
		private readonly int menuId;

		/// <summary>
		/// Click handlers for all menu items within this menu.
		/// Each item can have more than one click handler.
		/// </summary>
		private readonly ConcurrentDictionary<ToolStripItem, CallbackRegistry> clickHandlers = new();

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
				// AutoHotkey's set_Default reads a blank name as "no default" and looks nothing up; any other
				// name must name an item that exists.
				if (value?.Length != 0 && GetExistingMenuItem(value) is ToolStripMenuItem item)
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
		/// The backing toolkit menu as an ordinary <c>Ks.Clr</c> object. Its concrete type is platform-dependent
		/// and unspecified; changes made through it bypass this class's own state and event wiring.
		/// </summary>
		public object ToClr() => ManagedInvoke.WrapManaged(GetMenu());

		/// <summary>
		/// The number of sub items contained in the menu.
		/// </summary>
		public long MenuItemCount => GetMenu().Items.Count;

		/// <summary>
		/// The <see cref="ContextMenuStrip"/> that holds the menu items.
		/// </summary>
		internal ContextMenuStrip MenuItem { get; set; }

		/// <summary>
		/// The drop-down this menu's items were handed over to when it became a submenu, or null while it stands
		/// on its own. AutoHotkey can use one menu in several places because every use shares its native menu,
		/// whereas here becoming a submenu moves the items into the owning item's drop-down. Every lookup by name
		/// or position therefore has to follow them, or it would search a collection the items just left.
		/// </summary>
		private ToolStrip submenuOf;

		/// <summary>
		/// Initializes a new instance of the <see cref="Menu"/> class.
		/// </summary>
		/// <param name="strip">Optional existing <see cref="ContextMenuStrip"/>. Default: false.</param>
		public Menu(params object[] args) : base(args)
		{
			MenuItem = (args?.Length > 0 ? (ContextMenuStrip)args[0] : null) ?? new ContextMenuStrip();
			//Virtual, so this also covers a MenuBar's MenuStrip: a derived class's field initializers have
			//already run by the time the base constructor body does.
			InitMenu(GetMenu());
			//GetMenu().ImageScalingSize = new System.Drawing.Size(28, 28);//Don't set scaling, it makes the checked icons look funny.
			menuId = Interlocked.Increment(ref Script.TheScript.GuiData.menuCount);
			Script.TheScript.GuiData.allMenus[menuId] = new(this);
			GetMenu().Name = $"Menu_{menuId}";
			dummyHandle = Handle;//Must access the handle once to force creation.
			// Track menu visibility (AutoHotkey's g_MenuIsVisible) so timers are held and the keyboard hook passes
			// keystrokes through while this menu is open. Visibility is reference-counted per menu id, so overlapping
			// menus don't clobber each other. Closed fires on every backend; Opened only on WinForms, so the other
			// backends set the flag in Show() instead.
			MenuItem.Closed += (_, _) => Script.TheScript.SetMenuVisible(menuId, false);
#if WINDOWS
			MenuItem.Opened += (_, _) => Script.TheScript.SetMenuVisible(menuId, true);
			// If the strip is disposed while still marked open (e.g. a non-blocking Show that never raised Closed),
			// release its contribution so a torn-down menu can't strand the count and hold every timer forever.
			MenuItem.Disposed += (_, _) => Script.TheScript.SetMenuVisible(menuId, false);
#endif
		}

		internal bool RemoveOwnedHandlers(ScriptEventScheduler scheduler)
			=> CallbackRegistry.RemoveOwned(clickHandlers, scheduler);

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
				return DefaultObject;
			}

			return AddOrInsert("", menuItemName.As(), callbackOrSubmenu, options.As(), false);
		}

		/// <summary>
		/// Adds the standard tray menu items after any existing items.<br/>
		/// Any standard items already in the menu are not duplicated, but any missing items are added.
		/// </summary>
		public object AddStandard()
		{
			var menu = GetMenu();
			var script = Script.TheScript;
			var openfunc = (params object[] args) =>
			{
				var mainWindow = script.mainWindow;

				if (mainWindow != null && A_AllowMainWindow.Ab())
				{
					script.PostToUIThread(() =>
					{
						mainWindow.AllowShowDisplay = true;
						mainWindow.WindowState = mainWindow.lastWindowState == FormWindowState.Minimized
							? FormWindowState.Normal
							: mainWindow.lastWindowState;
						mainWindow.Show();
						mainWindow.Visible = true;
						mainWindow.BringToFront();
						mainWindow.Focus();
						// The tab that comes back into view holds a snapshot taken the last time it was
						// refreshed (nothing at all, on a first open), so regenerate it now.
						mainWindow.RefreshSelectedTab();
					});
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
				_ = Keysharp.Internals.Flow.ExitAppInternal(script, Flow.ExitReasons.Menu, null, true);
				return DefaultObject;
			};
			//Won't be a gui target, so won't be marked as IsGui internally, but it's ok because it's only ever called on the gui thread in response to gui events.
			script.openMenuItem = (ToolStripMenuItem)Add("&Open", new KeysharpFunc(openfunc.Method, openfunc.Target));

			if (!A_AllowMainWindow.Ab())
				script.openMenuItem.Visible = false;

			var helpFunc = (params object[] args) =>
			{
				_ = Processes.Run("https://github.com/keysharp-org/Keysharp/issues");
				return DefaultObject;
			};
			_ = Add("&Help", new KeysharpFunc(helpFunc.Method, helpFunc.Target));

			if (menu.Items.Cast<ToolStripItem>().Any(tsi => tsi.Visible))
				_ = menu.Items.Add(new ToolStripSeparator());

			// Resolve a bundled helper script (WindowSpy/AtSpi/Ax) from its "Scripts" folder.
			// Normally that folder sits beside the running executable, but A_AhkPath is the
			// process host -- which is the dotnet host (e.g. /usr/lib/dotnet/dotnet) when Keysharp
			// is launched via "dotnet" while debugging from an IDE. In that case fall back to the
			// directory of Keysharp.Core.dll, where the Scripts folder is also copied in build output.
			static string ResolveBundledScript(string name)
			{
				foreach (var baseDir in new[] { Path.GetDirectoryName(Accessors.A_AhkPath), Path.GetDirectoryName(Ks.A_KsCorePath) })
				{
					if (string.IsNullOrEmpty(baseDir))
						continue;

					var compiled = Path.Combine(baseDir, "Scripts", name + ".cks");//Prefer the precompiled .cks for faster startup.

					if (File.Exists(compiled))
						return compiled;

					var source = Path.Combine(baseDir, "Scripts", name + ".ks");

					if (File.Exists(source))
						return source;
				}

				return null;
			}

			var windowSpyFunc = (params object[] args) =>
			{
				var spy = ResolveBundledScript("WindowSpy");

				if (spy == null)
				{
					_ = Dialogs.MsgBox($"Window Spy script not found in a Scripts folder beside:\n{Accessors.A_AhkPath}\nor\n{Ks.A_KsCorePath}", "Keysharp", "Icon!");
					return DefaultObject;
				}

				Ks.RunScript(spy, true);//Run async so the calling script isn't blocked while Window Spy is open.
				return DefaultObject;
			};
			_ = Add("&Window Spy", new KeysharpFunc(windowSpyFunc.Method, windowSpyFunc.Target));
#if LINUX || OSX
#if LINUX
			const string accessibilitySpyName = "AtSpi";
#else
			const string accessibilitySpyName = "Ax";
#endif
			var accessibilitySpyFunc = (params object[] args) =>
			{
				var spy = ResolveBundledScript(accessibilitySpyName);

				if (spy == null)
				{
					_ = Dialogs.MsgBox($"{accessibilitySpyName} accessibility inspector script not found in a Scripts folder beside:\n{Accessors.A_AhkPath}\nor\n{Ks.A_KsCorePath}", "Keysharp", "Icon!");
					return DefaultObject;
				}

				Ks.RunScript(spy, true);
				return DefaultObject;
			};
			_ = Add($"&Accessibility Spy", new KeysharpFunc(accessibilitySpyFunc.Method, accessibilitySpyFunc.Target));
			_ = menu.Items.Add(new ToolStripSeparator());
#endif
			_ = Add("&Reload Script", new KeysharpFunc(reloadfunc.Method, reloadfunc.Target));

			if (!A_IsCompiled)
			{
				var editfunc = (params object[] args) =>
				{
					_ = Debug.Edit();
					return DefaultObject;
				};
				_ = Add("&Edit Script", new KeysharpFunc(editfunc.Method, editfunc.Target));
			}

			_ = menu.Items.Add(new ToolStripSeparator());
			script.suspendMenuItem = (ToolStripMenuItem)Add("&Suspend Hotkeys", new KeysharpFunc(suspend.Method, suspend.Target));
			_ = Add("E&xit", new KeysharpFunc(exitfunc.Method, exitfunc.Target));
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
		public object Delete(object menuItemName = null)
		{
			var s = menuItemName.As();

			if (s?.Length == 0)
			{
				GetMenu().Items.Clear();
				foreach (var hub in clickHandlers.Values)
					hub.Clear();

				clickHandlers.Clear();
			}
			else if (GetExistingMenuItem(s) is ToolStripItem item)
			{
				if (item == defaultItem)
					defaultItem = null;

				if (item.GetCurrentParent() is ToolStripDropDownMenu tsddm)
					tsddm.Items.Remove(item);
				else
					GetMenu().Items.Remove(item);

				GetMenu().Refresh();
				_ = clickHandlers.TryRemove(item, out _);
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
		public object Insert(object menuItemName = null, object itemToInsert = null, object callbackOrSubmenu = null, object options = null) => AddOrInsert(menuItemName.As(), itemToInsert.As(), callbackOrSubmenu, options.As(), true);

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
			var item = GetExistingMenuItem(name);

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

			if (GetExistingMenuItem(name) is ToolStripItem tsmi)
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
		/// <param name="wait">If true or omitted, wait until the menu is closed before returning. If false, return immediately.</param>
		public object Show(object x = null, object y = null, object wait = null)
		{
			if (this is MenuBar)
				return Errors.ValueErrorOccurred("MenuBar objects cannot be shown as popup menus.");

			// Keysharp menus do not expose the native MNS_MODELESS style, so an omitted Wait has the standard-menu
			// default of true (AHK's aWait.value_or(temp_modeless)). Passing false retains the non-blocking form.
			var shouldWait = wait.Ab(true);

			Script.TheScript.InvokeOnUIThread(() =>
			{
				_ = GetCursorPos(out POINT def);
				var _x = x.Ai();
				var _y = y.Ai();
				if (x != null || y != null) CoordToScreen(ref _x, ref _y, Builtins.CoordMode.Menu);
				if (x == null) _x = def.X;
				if (y == null) _y = def.Y;
				var pt = new Point(_x, _y);
#if !WINDOWS
				// Non-WinForms backends don't raise Opened, so mark the menu visible here (the ctor's Closed handler
				// clears it). WinForms uses Opened/Closed for this and needs nothing extra.
				Script.TheScript.SetMenuVisible(menuId, true);
#endif

				if (!shouldWait)
				{
					MenuItem.Show(pt);
					return;
				}

				var closed = false;

#if WINDOWS
				ToolStripDropDownClosedEventHandler handler = (_, _) => closed = true;
#else
				EventHandler<EventArgs> handler = (_, _) => closed = true;
#endif
				try
				{
					MenuItem.Closed += handler;
					MenuItem.Show(pt);
#if WINDOWS
					Keysharp.Internals.Flow.WaitWithMessagePump(() => !closed && !MenuItem.IsDisposed && MenuItem.Visible);
#else
					Keysharp.Internals.Flow.WaitWithMessagePump(() => !closed);
#endif
				}
				finally
				{
					MenuItem.Closed -= handler;
				}
			});
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
		public bool UnCheck(object menuItemName) => Check(menuItemName.As(), eCheckToggle.Uncheck);

		internal void Tsmi_Click(object sender, EventArgs e)
		{
			// The menu is closing as this item is chosen; clear visibility up front so the callback (and any timer it
			// starts) isn't held by the menu-visible guard, regardless of whether Click or Closed fires first. Matches
			// AutoHotkey clearing g_MenuIsVisible before the menu item's subroutine runs.
			Script.TheScript.SetMenuVisible(menuId, false);

			if (sender is ToolStripMenuItem tsmi)
			{
				if (clickHandlers.TryGetValue(tsmi, out var handler))
				{
					var index = GetIndex(tsmi);
					_ = handler.InvokeEventHandlers(tsmi.Text, ++index, this);
				}
			}
		}

		protected internal virtual ToolStrip GetMenu() => submenuOf ?? MenuItem;

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

			return DefaultObject;
		}

		/// <summary>
		/// The item <paramref name="s"/> names, raising the way AutoHotkey's UserMenu::GetItem does when the menu
		/// holds no such item. Every method that acts ON an item goes through this; <see cref="GetMenuItem"/> stays
		/// the tolerant finder, for the callers where a miss is a legal answer rather than a mistake.
		/// </summary>
		protected object GetExistingMenuItem(string s)
			=> GetMenuItem(s) is ToolStripItem item ? item : Errors.TargetErrorOccurred($"Nonexistent menu item: {s}");

		/// <param name="insert">Whether the call came from Insert rather than Add. AutoHotkey looks an existing item
		/// up for Add alone (script_menu.cpp's `if (!aInsertAt)`), so Insert always creates one — with no anchor it
		/// appends — and its callback sits one parameter later.</param>
		private object AddOrInsert(string insertbefore, string name, object funcorsub, string options, bool insert)
		{
			ToolStripMenuItem item = null;
			// Only an EXISTING item may have its options retargeted with the callback omitted, so Insert always
			// requires one. Both the lookup and the check happen before anything is added, so a rejected call
			// leaves the menu exactly as it was rather than stranding an item that does nothing when chosen.
			var existing = !insert && !string.IsNullOrEmpty(name)
						   ? GetMenu().Items.Find(name, true).FirstOrDefault() as ToolStripMenuItem
						   : null;
			var canOmitCallback = existing != null && options.Length > 0;

			// A blank name adds a separator, which never carries a callback.
			if (funcorsub == null && !string.IsNullOrEmpty(name) && !canOmitCallback)
				return Errors.ArgumentErrorOccurred(funcorsub, insert ? 3 : 2);

			if (!string.IsNullOrEmpty(insertbefore))
			{
				// The anchor has to exist; AutoHotkey's Insert raises ItemNotFoundError otherwise. Going through
				// the shared lookup also accepts the "3&" position form and a separator as the anchor.
				if (GetExistingMenuItem(insertbefore) is ToolStripItem tsmiinsert)
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
			else if (existing != null)
			{
				item = existing;
			}
			else if (GetMenu().Items.Add(name) is ToolStripMenuItem tsmi2)
			{
				tsmi2.Click += Tsmi_Click;
				tsmi2.Name = name;
				item = tsmi2;
			}

			if (item != null)
			{
				if (string.IsNullOrEmpty(Default) && item.Text == "&Open")
				{
					Default = "&Open";
				}

				Keysharp.Internals.Scripting.CallbackRegistration clickReg = null;

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

					//The submenu's drop-down only comes into existence here, so this is where it gets its renderer
					//and column handling. Items that already own a populated drop-down keep the one they were given.
					InitMenu(item.DropDown);
					//Point the submenu at where its items now live. This has to come after the move, which drains
					//whichever collection the menu was using up to now.
					mnu.submenuOf = item.DropDown;
#if !WINDOWS
					item.Owner?.SyncEtoItems();
#endif
				}
				// An options-only call registers nothing, leaving the item's existing handler in place — the guard
				// above has already established that omitting the callback is legal only in that case. AutoHotkey's
				// ModifyItem does the same by returning before it assigns mCallback.
				else if (funcorsub != null)
				{
					// Create the registration explicitly (not ModifyEventHandlers) so the "Pn" option parsed below can
					// set its Priority — the priority then travels with the registration to the launch.
					clickReg = new Keysharp.Internals.Scripting.CallbackRegistration(Functions.GetKeysharpFunc(funcorsub, null, true), Script.TheScript.EventScheduler, true);
					clickHandlers.GetOrAdd(item, static _ => new()).Add(clickReg);
				}

				foreach (Range r in options.AsSpan().SplitAny(Spaces))
				{
					var opt = options.AsSpan(r).Trim();

					if (opt.Length > 0)
					{
						var temp = 0;
						var tempbool = false;

						if (Options.TryParse(opt, "P", ref temp)) { if (clickReg != null) clickReg.Priority = temp; }
						else if (Options.TryParse(opt, "Radio", ref tempbool, StringComparison.OrdinalIgnoreCase, true, true))
							GetPresentation(item).Radio = tempbool;
						else if (Options.TryParse(opt, "Right", ref tempbool, StringComparison.OrdinalIgnoreCase, true, true))
						{
							//AHK honors Right only for menu-bar items (MENU_TYPE_BAR), not inside popups or submenus.
							//Items can be inserted into a submenu through a MenuBar, so the parent has to be checked too.
							if (this is MenuBar && item.GetCurrentParent() == GetMenu())
							{
								GetPresentation(item).Right = tempbool;
#if WINDOWS
								item.Alignment = tempbool ? ToolStripItemAlignment.Right : ToolStripItemAlignment.Left;
#endif
							}
						}
						else if (Options.TryParse(opt, "Break", ref tempbool, StringComparison.OrdinalIgnoreCase, true, true))
							SetColumnBreak(item, tempbool, false);
						else if (Options.TryParse(opt, "BarBreak", ref tempbool, StringComparison.OrdinalIgnoreCase, true, true))
							SetColumnBreak(item, tempbool, true);
						else if (Options.TryParse(opt, "RTL", ref tempbool, StringComparison.OrdinalIgnoreCase, true, true))
						{
							GetPresentation(item).Rtl = tempbool;
#if WINDOWS
							item.RightToLeft = tempbool ? RightToLeft.Yes : RightToLeft.No;
#endif
						}
					}
				}

#if !WINDOWS
				//The Eto backends have no "about to open" hook — the tray indicator and menu bar are handed the
				//native menu directly — so their items must be rebuilt as soon as the menu changes. On Windows
				//this is deferred to the drop-down's Opening event (see InitMenu).
				GetMenu().Refresh();
#endif
			}

			return item != null ? item : "";
		}

		private bool Check(string s, eCheckToggle checktoggle)
		{
			if (GetExistingMenuItem(s) is ToolStripMenuItem item)
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
			if (GetExistingMenuItem(s) is ToolStripItem item)
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
			if (GetExistingMenuItem(s) is ToolStripMenuItem item)
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
		public MenuBar(params object[] args) : base(args) { }

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
