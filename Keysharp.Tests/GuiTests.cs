using System.Collections.Concurrent;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using Keysharp.Internals;
using Keysharp.Internals.Images;
#if OSX
using AppKit = MonoMac.AppKit;
#endif

namespace Keysharp.Tests
{
	[Category("Internal")]
	public class GuiTests : TestRunner
	{
		private const string MsgBoxTitle = "this is a sample title";

		[Test, Category("Gui")]
		public void DisplaySelection()
		{
			DisplayInfo[] displays =
			[
				new("primary", new ScreenRect(0, 0, 1920, 1080), new ScreenRect(0, 0, 1920, 1040), 1.0, true),
				new("left", new ScreenRect(-2560, 0, 2560, 1440), new ScreenRect(-2560, 0, 2560, 1400), 1.5, false),
				new("above", new ScreenRect(0, -1200, 1600, 1200), new ScreenRect(0, -1200, 1600, 1160), 2.0, false)
			];

			Assert.IsTrue(DisplayTopology.TryFind(displays, new ScreenRect(-300, 100, 400, 300), out var spanning));
			Assert.AreEqual("left", spanning.Name, "largest visible intersection should own a spanning overlay");

			Assert.IsTrue(DisplayTopology.TryFind(displays, new ScreenRect(100, -100, 0, 0), out var point));
			Assert.AreEqual("above", point.Name, "zero-size point lookup should honor negative monitor origins");

			Assert.IsTrue(DisplayTopology.TryFind(displays, new ScreenRect(4000, 100, 20, 20), out var nearest));
			Assert.AreEqual("primary", nearest.Name, "off-desktop placement should use the nearest display deterministically");

			DisplayInfo[] unequalDisplays =
			[
				new("large", new ScreenRect(0, 0, 4000, 1000), new ScreenRect(0, 0, 4000, 1000), 1, true),
				new("small", new ScreenRect(4200, 0, 100, 1000), new ScreenRect(4200, 0, 100, 1000), 1, false)
			];
			Assert.IsTrue(DisplayTopology.TryFind(unequalDisplays, new ScreenRect(4050, 500, 0, 0), out var edgeNearest));
			Assert.AreEqual("large", edgeNearest.Name, "nearest lookup must compare display edges, not centres");
		}

		[Test, Category("Gui")]
		public void ExtremeDisplayInput()
		{
			DisplayInfo[] displays =
			[
				new("left", new ScreenRect(int.MinValue, -10, 100, 20), new ScreenRect(int.MinValue, -10, 100, 20), 1, false),
				new("right", new ScreenRect(int.MaxValue - 99, -10, 100, 20), new ScreenRect(int.MaxValue - 99, -10, 100, 20), 1, true)
			];

			Assert.IsTrue(DisplayTopology.TryFind(displays, new ScreenRect(int.MinValue, 0, int.MaxValue, 1), out var selected));
			Assert.AreEqual("left", selected.Name);
			Assert.AreEqual((long)int.MaxValue + 1, new ScreenRect(int.MaxValue, 0, 1, 1).Right);
		}

		[Test, Category("Gui")]
		public void WindowsCaptureScale()
		{
			// PMv2 Windows exposes both monitors in physical desktop pixels. A 1920-wide 100% display followed
			// by a 2560-wide 150% display is therefore one 4480-pixel-wide capture; UI scale is not applied here.
			var desktop = new ScreenRect(0, 0, 4480, 1440);
			var pixels = new PixelSize(4480, 1440);

			Assert.AreEqual(new Point(2920, 700), desktop.PixelToScreen(new Point(2920, 700), pixels));
			Assert.AreEqual(new Point(2920, 700), desktop.ScreenToPixel(2920, 700, pixels));
		}

		[Test, Category("Gui")]
		public void DenseCaptureScale()
		{
			var twice = new ScreenRect(10, 20, 2, 1);
			var fractional = new ScreenRect(10, 20, 2, 2);
			var fiveForFour = new ScreenRect(10, 20, 4, 1);
			var fractionalSeam = new ScreenRect(0, 0, 6, 1);
			var roundedCanvasSeam = new ScreenRect(0, 0, 5, 1);
			// A 1920-point non-Retina display beside a 1440-point Retina display is flattened to a
			// 6720-pixel 2x canvas. X=4840 is 500 logical points into the second display.
			var mixedMacDesktop = new ScreenRect(0, 0, 3360, 1080);

			Assert.AreEqual(new Point(11, 20), twice.PixelToScreen(new Point(3, 1), new PixelSize(4, 2)));
			Assert.AreEqual(new Point(11, 21), fractional.PixelToScreen(new Point(1, 1), new PixelSize(3, 3)));
			Assert.AreEqual(new Point(11, 21), fractional.PixelToScreen(new Point(2, 2), new PixelSize(3, 3)));
			Assert.AreEqual(new Point(11, 21), fractional.PixelToScreen(
				fractional.ScreenToPixel(11, 21, new PixelSize(3, 3)), new PixelSize(3, 3)));
			Assert.AreEqual(new Point(13, 20), fiveForFour.PixelToScreen(
				fiveForFour.ScreenToPixel(13, 20, new PixelSize(5, 1)), new PixelSize(5, 1)));
			Assert.AreEqual(new Point(3, 0), fractionalSeam.PixelToScreen(new Point(4, 0), new PixelSize(9, 1)));
			Assert.AreEqual(new Point(2, 0), roundedCanvasSeam.PixelToScreen(new Point(4, 0), new PixelSize(8, 1)));
			Assert.AreEqual(new Point(3, 0), roundedCanvasSeam.PixelToScreen(new Point(5, 0), new PixelSize(8, 1)));
			Assert.AreEqual(new Point(2420, 300), mixedMacDesktop.PixelToScreen(
				new Point(4840, 600), new PixelSize(6720, 2160)));
		}

		[Test, Category("Gui")]
		public void HiddenOverlayUpdate()
		{
			using var image = KeysharpImage.Create(null, 20, 12, "0xFF204060") as KeysharpImage;
			var overlay = new Ks.KeysharpOverlay();
			_ = overlay.__New(1L, 2L, 20L, 12L);

			try
			{
				_ = overlay.Update(image, 31L, 42L, 30L, 18L);

				Assert.AreEqual(31L, overlay.X);
				Assert.AreEqual(42L, overlay.Y);
				Assert.AreEqual(30L, overlay.Width);
				Assert.AreEqual(18L, overlay.Height);
				Assert.AreEqual(false, overlay.Visible);
			}
			finally
			{
				_ = overlay.Destroy();
			}
		}

		[Test, Category("Gui")]
		public void OverlayRedraw()
		{
			var overlay = new Ks.KeysharpOverlay();
			_ = overlay.__New(1L, 2L, 10L, 10L);

			try
			{
				var callback = new KeysharpFunc((Func<object, object>)(target =>
				{
					_ = ((Ks.KeysharpOverlay)target).Clear("0xFF204060");
					return 0L;
				}));
				_ = overlay.Redraw(callback, 31L, 42L, 30L, 18L);

				Assert.AreEqual(31L, overlay.X);
				Assert.AreEqual(42L, overlay.Y);
				Assert.AreEqual(30L, overlay.Width);
				Assert.AreEqual(18L, overlay.Height);
				Assert.AreEqual(false, overlay.Visible);
			}
			finally
			{
				_ = overlay.Destroy();
			}
		}

		[Test, Category("Gui")]
		public void OverlayImageSize()
		{
			using var image = KeysharpImage.Create(null, 20, 12, "0xFF204060", 2.0) as KeysharpImage;
			var overlay = new Ks.KeysharpOverlay();
			_ = overlay.__New(1L, 2L);

			try
			{
				_ = overlay.Update(image);
				Assert.AreEqual(40L, overlay.Width);
				Assert.AreEqual(24L, overlay.Height);
			}
			finally
			{
				_ = overlay.Destroy();
			}
		}

#if WINDOWS
		[Test, Category("Gui")]
		[Apartment(ApartmentState.STA)]
		public void DpiResizeDefaults()
		{
			var gui = new Gui(System.Array.Empty<object>());
			_ = gui.__New();

			try
			{
				// WinForms must never scale on its own, otherwise "-DPIScale" raw pixels get scaled anyway and
				// every child is resized regardless of its own "-DPIResize".
				Assert.AreEqual(AutoScaleMode.None, gui.form.AutoScaleMode);
				_ = gui.Opt("-DPIScale");
				Assert.AreEqual(AutoScaleMode.None, gui.form.AutoScaleMode);

				var inherited = (Gui.Control)gui.Add("Button", "x10 y10 w100 h30");
				var optedOut = (Gui.Control)gui.Add("Button", "x10 y50 w100 h30 -DPIResize");
				Assert.IsTrue(inherited.dpiResize, "controls should inherit the GUI's default");
				Assert.IsFalse(optedOut.dpiResize, "a per-control -DPIResize should override the GUI default");

				// As in AHK, the GUI option only seeds controls added afterwards.
				_ = gui.Opt("-DPIResize");
				Assert.IsTrue(inherited.dpiResize, "changing the GUI default must not affect existing controls");
				var addedAfter = (Gui.Control)gui.Add("Button", "x10 y90 w100 h30");
				Assert.IsFalse(addedAfter.dpiResize);

				// GuiCtrl.Opt() can flip it back on an existing control.
				_ = optedOut.Opt("+DPIResize");
				Assert.IsTrue(optedOut.dpiResize);
			}
			finally
			{
				_ = gui.Destroy();
			}
		}

		[Test, Category("Gui")]
		[Apartment(ApartmentState.STA)]
		public void DpiOptOut()
		{
			var gui = new Gui(System.Array.Empty<object>());
			_ = gui.__New();

			try
			{
				var scaled = (Gui.Control)gui.Add("Button", "w100 h30");
				var unscaled = (Gui.Control)gui.Add("Button", "w100 h30 -DPIResize");
				// Bounds are assigned explicitly so the expected values don't depend on Keysharp's layout math.
				var original = new Rectangle(10, 10, 100, 30);
				scaled.Ctrl.Bounds = original;
				unscaled.Ctrl.Bounds = original;

				gui.RescaleForDpi(96, 144);
				Assert.AreEqual(new Rectangle(15, 15, 150, 45), scaled.Ctrl.Bounds);
				Assert.AreEqual(original, unscaled.Ctrl.Bounds, "-DPIResize controls must keep their bounds");

				// Scaling back down must land on the original bounds rather than drifting.
				gui.RescaleForDpi(144, 96);
				Assert.AreEqual(original, scaled.Ctrl.Bounds);
				Assert.AreEqual(original, unscaled.Ctrl.Bounds);

				// A no-op transition must not touch anything.
				gui.RescaleForDpi(96, 96);
				Assert.AreEqual(original, scaled.Ctrl.Bounds);
			}
			finally
			{
				_ = gui.Destroy();
			}
		}

		[Test, Category("Gui")]
		[Apartment(ApartmentState.STA)]
		public void DpiChangeEvent()
		{
			var gui = new Gui(System.Array.Empty<object>());
			_ = gui.__New();
			var callback = new KeysharpFunc((Func<object, object, object, object>)((_, _, _) => 0L));

			try
			{
				_ = gui.OnEvent("DPIChange", callback);
				Assert.AreEqual(1, gui.form.dpiChangeHandlers.Count);
				// Dispatch depends on the script thread scheduler; this verifies registration and the native event bridge.
				gui.form.CallDpiChangeHandlers(96, 144);
			}
			finally
			{
				_ = gui.Destroy();
			}
		}

		/// <summary>
		/// An <c>ObjBindMethod</c> handler carries no signature, which raised out of <c>IsClosure</c>, and its
		/// receiver was then cleared off the script's own function object. Registration must leave it as it was.
		/// </summary>
		[Test, Category("Gui")]
		[Apartment(ApartmentState.STA)]
		public void BoundEventHandler()
		{
			var gui = new Gui(System.Array.Empty<object>());
			_ = gui.__New();
			var target = new Keysharp.Builtins.Array();
			var handler = (KeysharpFunc)Functions.ObjBindMethod(target, "Push");

			try
			{
				_ = gui.OnEvent("Close", handler);
				Assert.AreEqual(1, gui.form.closedHandlers.Count);
				Assert.AreSame(target, handler.Inst, "registration must not detach the receiver");
				_ = handler.Call("fired");
				Assert.AreEqual(1, target.Count, "the handler must still reach its own receiver afterwards");
			}
			finally
			{
				_ = gui.Destroy();
			}
		}

		[Test, Category("Gui")]
		[Apartment(ApartmentState.STA)]
		public void MessageHandlers()
		{
			const int msgId = 0x8123;
			var gui = new Gui(System.Array.Empty<object>());
			_ = gui.__New();

			try
			{
				var order = new List<string>();
				var control = (Gui.Control)gui.Add("Button", "w80 h24");
				// Returning "" (empty) is what lets the chain continue; see the explicit-0 case further down.
				var first = new KeysharpFunc((Func<object, object, object, object, object>)((_, _, _, _) => { order.Add("first"); return ""; }));
				var second = new KeysharpFunc((Func<object, object, object, object, object>)((_, _, _, _) => { order.Add("second"); return ""; }));

				// AddRemove is an ordering switch, not a thread count: AHK rejects anything outside -1..1.
				_ = Assert.Throws<Keysharp.Builtins.KeysharpException>(() => gui.OnMessage(msgId, second, 5L));
				_ = Assert.Throws<Keysharp.Builtins.KeysharpException>(() => control.OnMessage(msgId, second, -2L));

				_ = gui.OnMessage(msgId, second);        // default 1: append
				_ = gui.OnMessage(msgId, first, -1L);    // -1: run before the already-registered one

				var m = Message.Create(gui.form.Handle, msgId, 0, 0);
				Assert.IsFalse(gui.InvokeWindowMessageHandlers(ref m), "handlers returning \"\" must not claim the message");
				NUnit.Framework.Legacy.CollectionAssert.AreEqual(new[] { "first", "second" }, order);

				// A non-empty return claims the message and supplies its result, skipping later handlers.
				order.Clear();
				var claiming = new KeysharpFunc((Func<object, object, object, object, object>)((_, _, _, _) => { order.Add("claim"); return 7L; }));
				_ = gui.OnMessage(msgId, claiming, -1L);
				m = Message.Create(gui.form.Handle, msgId, 0, 0);
				Assert.IsTrue(gui.InvokeWindowMessageHandlers(ref m));
				Assert.AreEqual(7, m.Result.ToInt64());
				NUnit.Framework.Legacy.CollectionAssert.AreEqual(new[] { "claim" }, order);

				// 0 unregisters.
				order.Clear();
				_ = gui.OnMessage(msgId, claiming, 0L);
				_ = gui.OnMessage(msgId, first, 0L);
				_ = gui.OnMessage(msgId, second, 0L);
				m = Message.Create(gui.form.Handle, msgId, 0, 0);
				Assert.IsFalse(gui.InvokeWindowMessageHandlers(ref m));
				NUnit.Framework.Legacy.CollectionAssert.IsEmpty(order);

				// Only an EMPTY return leaves the message unclaimed, per AHK. An empty string and no return at
				// all both let the next handler (and then the default window procedure) run.
				foreach (var inert in new object[] { "", null })
				{
					order.Clear();
					var quiet = new KeysharpFunc((Func<object, object, object, object, object>)((_, _, _, _) => { order.Add("quiet"); return inert; }));
					_ = gui.OnMessage(msgId, quiet);
					_ = gui.OnMessage(msgId, second);
					m = Message.Create(gui.form.Handle, msgId, 0, 0);
					Assert.IsFalse(gui.InvokeWindowMessageHandlers(ref m), $"a return of '{inert ?? "(no return)"}' must not claim the message");
					NUnit.Framework.Legacy.CollectionAssert.AreEqual(new[] { "quiet", "second" }, order, "an unclaimed message must still reach later handlers");
					Assert.AreEqual(0, m.Result.ToInt64());
					_ = gui.OnMessage(msgId, quiet, 0L);
					_ = gui.OnMessage(msgId, second, 0L);
				}

				// An explicit 0 IS non-empty, so it claims the message and replies 0 — the distinction that
				// separates AHK's rule from a plain non-zero test.
				order.Clear();
				var repliesZero = new KeysharpFunc((Func<object, object, object, object, object>)((_, _, _, _) => { order.Add("zero"); return 0L; }));
				_ = gui.OnMessage(msgId, repliesZero);
				_ = gui.OnMessage(msgId, second);
				m = Message.Create(gui.form.Handle, msgId, 0, 0);
				Assert.IsTrue(gui.InvokeWindowMessageHandlers(ref m), "an explicit 0 is non-empty and claims the message");
				Assert.AreEqual(0, m.Result.ToInt64(), "the claimed message replies with the returned 0");
				NUnit.Framework.Legacy.CollectionAssert.AreEqual(new[] { "zero" }, order, "claiming must skip the remaining handlers");
				_ = gui.OnMessage(msgId, repliesZero, 0L);
				_ = gui.OnMessage(msgId, second, 0L);

				// Controls keep their own registry, keyed by message rather than by notification code.
				var ctrlHits = 0;
				var ctrlHandler = new KeysharpFunc((Func<object, object, object, object, object>)((_, _, _, _) => { ctrlHits++; return 3L; }));
				_ = control.OnMessage(msgId, ctrlHandler);
				var cm = Message.Create(control.Ctrl.Handle, msgId, 0, 0);
				Assert.AreEqual(1L, control.InvokeMessageHandlers(ref cm).Al());
				Assert.AreEqual(1, ctrlHits);
				Assert.AreEqual(3, cm.Result.ToInt64());
			}
			finally
			{
				_ = gui.Destroy();
			}
		}

		[Test, Category("Gui")]
		[Apartment(ApartmentState.STA)]
		public void ColumnedMenuSize()
		{
			// Mirrors guitest.ks's Presentation menu: a MenuBar submenu with radio items, a separator and two
			// column breaks. A columned menu is sized explicitly, so anything measured from the laid-out bounds
			// feeds back in and the menu grows a little every time it is shown.
			var callback = new KeysharpFunc((Func<object, object, object, object>)((_, _, _) => 0L));
			var menu = new Keysharp.Builtins.Menu();
			_ = menu.Add("Radio choice &1", callback, "Radio");
			_ = menu.Add("Radio choice &2", callback, "Radio");
			_ = menu.Add("Radio choice &3", callback, "Radio");
			_ = menu.Check("Radio choice &1");
			_ = menu.Add();
			var firstColumn = (ToolStripMenuItem)menu.Add("Toggle my chec&kmark", callback);
			_ = menu.Add("Break: column two", callback, "Break");
			_ = menu.Add("Second-column item", callback);
			// Deliberately the widest item in the menu, and in the last column rather than the first.
			var lastColumn = (ToolStripMenuItem)menu.Add("BarBreak: column three", callback, "BarBreak");
			_ = menu.Add("Third-column item", callback);

			var menuBar = new Keysharp.Builtins.MenuBar();
			var host = (ToolStripMenuItem)menuBar.Add("&Presentation", menu, "Right");
			var drop = host.DropDown;

			try
			{
				drop.Show(new Point(0, 0));
				drop.Close();
				var initial = drop.Size;

				// Each column is sized to its own widest item, not to the widest in the whole menu, so a column
				// whose longest label is shorter must come out narrower.
				Assert.Less(firstColumn.Width, lastColumn.Width,
					"columns must be sized independently, not all to the widest item in the menu.");

				// A separator belongs to one column, as a native menu draws it. WinForms stretches it across the
				// whole window regardless of the width it is given, so the renderer clips it to this width.
				var separator = drop.Items.OfType<ToolStripSeparator>().Single();
				Assert.AreEqual(firstColumn.Width, Keysharp.Builtins.Menu.GetPresentation(separator).ColumnWidth,
					"a separator must be clipped to the column it sits in.");
				Assert.Greater(separator.Width, firstColumn.Width,
					"this assumes WinForms is still stretching separators; if not, the clipping is dead code.");

				for (var i = 0; i < 3; i++)
				{
					// What guitest.ks's PickPresentationRadio does on every selection.
					for (var n = 1; n <= 3; n++)
						_ = menu.UnCheck($"Radio choice &{n}");

					_ = menu.Check($"Radio choice &{(i % 3) + 1}");
					drop.Show(new Point(0, 0));
					drop.Close();
					Assert.AreEqual(initial, drop.Size, $"the menu changed size on display {i + 2}.");
				}
			}
			finally
			{
				menuBar.MenuStrip.Dispose();
				menuBar.MenuItem.Dispose();
				menu.MenuItem.Dispose();
			}
		}

		[Test, Category("Gui")]
		[Apartment(ApartmentState.STA)]
		public void MenuOptions()
		{
			var callback = new KeysharpFunc((Func<object, object, object, object>)((_, _, _) => 0L));
			var menu = new Keysharp.Builtins.Menu();
			var plainMenu = new Keysharp.Builtins.Menu();
			var subMenu = new Keysharp.Builtins.Menu();
			var menuBar = new Keysharp.Builtins.MenuBar();

			try
			{
				var first = (ToolStripMenuItem)menu.Add("First", callback);
				var second = (ToolStripMenuItem)menu.Add("Second", callback, "Radio BarBreak RTL");
				_ = menu.Check("Second");

				Assert.IsTrue(second.Checked);
				Assert.AreEqual(RightToLeft.Yes, second.RightToLeft);
				var presentation = Keysharp.Builtins.Menu.GetPresentation(second);
				Assert.IsTrue(presentation.Radio);
				Assert.IsTrue(presentation.BarBreak);
				Assert.IsTrue(presentation.StartsColumn);

				// Break and BarBreak are mutually exclusive; removing one must leave the other alone.
				_ = menu.Add("Second", callback, "Break");
				Assert.IsTrue(presentation.Break);
				Assert.IsFalse(presentation.BarBreak);
				_ = menu.Add("Second", callback, "-BarBreak");
				Assert.IsTrue(presentation.Break, "-BarBreak must not clear Break.");

				// Columns are arranged when the drop-down opens, not on every Add, so drive a real open.
				var flow = (FlowLayoutSettings)menu.MenuItem.LayoutSettings;
				Assert.IsFalse(flow.GetFlowBreak(first), "Adding items must not lay out a menu that is not shown.");
				OpenAndClose(menu.MenuItem);
				Assert.IsTrue(flow.GetFlowBreak(first), "A column break should begin a new column after the preceding item.");
				Assert.IsTrue(flow.WrapContents);
				Assert.Greater(second.Bounds.Left, first.Bounds.Left);

				// Losing the last column break must restore ordinary single-column autosizing.
				_ = menu.Add("Second", callback, "-Break");
				OpenAndClose(menu.MenuItem);
				Assert.IsFalse(flow.WrapContents);
				Assert.IsTrue(menu.MenuItem.AutoSize);
				Assert.Greater(second.Bounds.Top, first.Bounds.Top);

				// A menu that never asks for columns must be left entirely to WinForms' own layout.
				var plainFirst = (ToolStripMenuItem)plainMenu.Add("First", callback);
				var plainSecond = (ToolStripMenuItem)plainMenu.Add("Second", callback);
				OpenAndClose(plainMenu.MenuItem);
				Assert.IsFalse(((FlowLayoutSettings)plainMenu.MenuItem.LayoutSettings).WrapContents);
				Assert.Greater(plainSecond.Bounds.Top, plainFirst.Bounds.Top, "A normal popup menu should remain vertical.");

				// Right is a menu-bar-only option in AHK (MENU_TYPE_BAR); popups must ignore it.
				var right = (ToolStripMenuItem)menuBar.Add("Right", callback, "Right");
				Assert.AreEqual(ToolStripItemAlignment.Right, right.Alignment);
				Assert.IsTrue(Keysharp.Builtins.Menu.GetPresentation(right).Right);
				var notRight = (ToolStripMenuItem)plainMenu.Add("NotRight", callback, "Right");
				Assert.AreEqual(ToolStripItemAlignment.Left, notRight.Alignment);
				Assert.IsFalse(Keysharp.Builtins.Menu.GetPresentation(notRight).Right);

				// A submenu's drop-down is created on demand and must still get the Keysharp renderer. It reports
				// its owner's renderer rather than its own, so it also must not be mistaken for an already
				// initialized menu and left without column handling.
				var subFirst = (ToolStripMenuItem)subMenu.Add("First", callback);
				var subSecond = (ToolStripMenuItem)subMenu.Add("Second", callback, "BarBreak");
				var subItem = (ToolStripMenuItem)menuBar.Add("Sub", subMenu);
				Assert.AreSame(menuBar.MenuStrip.Renderer, subItem.DropDown.Renderer);
				OpenAndClose(subItem.DropDown);
				Assert.IsTrue(((FlowLayoutSettings)subItem.DropDown.LayoutSettings).WrapContents);
				Assert.Greater(subSecond.Bounds.Left, subFirst.Bounds.Left, "A menu bar's submenu must get columns too.");
			}
			finally
			{
				menu.MenuItem.Dispose();
				plainMenu.MenuItem.Dispose();
				subMenu.MenuItem.Dispose();
				menuBar.MenuStrip.Dispose();
				menuBar.MenuItem.Dispose();
			}
		}

		// Raises Opening/Closed exactly as a real display would, without leaving a popup on screen.
		private static void OpenAndClose(ToolStripDropDown menu)
		{
			menu.Show(new Point(0, 0));
			menu.Close();
		}

		[Test, Category("Gui")]
		[Apartment(ApartmentState.STA)]
		public void WinSetStyle()
		{
			using var form = new Form { Text = nameof(WinSetStyle) };
			var handle = form.Handle;
			var oldDetectHiddenWindows = A_DetectHiddenWindows;
			var originalStyle = WindowsAPI.GetWindowLongPtr(handle, WindowsAPI.GWL_STYLE).ToInt64();
			var originalExStyle = WindowsAPI.GetWindowLongPtr(handle, WindowsAPI.GWL_EXSTYLE).ToInt64();
			var newStyle = originalStyle ^ WindowsAPI.WS_DISABLED;

			try
			{
				A_DetectHiddenWindows = true;
				WindowX.WinSetStyle(newStyle, $"ahk_id {handle.ToInt64()}");

				Assert.AreEqual(newStyle, WindowsAPI.GetWindowLongPtr(handle, WindowsAPI.GWL_STYLE).ToInt64());
				Assert.AreEqual(originalExStyle, WindowsAPI.GetWindowLongPtr(handle, WindowsAPI.GWL_EXSTYLE).ToInt64());
			}
			finally
			{
				_ = WindowsAPI.SetWindowLongPtr(handle, WindowsAPI.GWL_STYLE, new nint(originalStyle));
				_ = WindowsAPI.SetWindowLongPtr(handle, WindowsAPI.GWL_EXSTYLE, new nint(originalExStyle));
				A_DetectHiddenWindows = oldDetectHiddenWindows;
			}
		}

		[Test, Category("Gui")]
		[Apartment(ApartmentState.STA)]
		public void WinSetExStyle()
		{
			using var form = new Form { Text = nameof(WinSetExStyle) };
			var handle = form.Handle;
			var oldDetectHiddenWindows = A_DetectHiddenWindows;
			var originalStyle = WindowsAPI.GetWindowLongPtr(handle, WindowsAPI.GWL_STYLE).ToInt64();
			var originalExStyle = WindowsAPI.GetWindowLongPtr(handle, WindowsAPI.GWL_EXSTYLE).ToInt64();
			var newExStyle = originalExStyle ^ WindowsAPI.WS_EX_TOOLWINDOW;

			try
			{
				A_DetectHiddenWindows = true;
				WindowX.WinSetExStyle(newExStyle, $"ahk_id {handle.ToInt64()}");

				Assert.AreEqual(originalStyle, WindowsAPI.GetWindowLongPtr(handle, WindowsAPI.GWL_STYLE).ToInt64());
				Assert.AreEqual(newExStyle, WindowsAPI.GetWindowLongPtr(handle, WindowsAPI.GWL_EXSTYLE).ToInt64());
			}
			finally
			{
				_ = WindowsAPI.SetWindowLongPtr(handle, WindowsAPI.GWL_STYLE, new nint(originalStyle));
				_ = WindowsAPI.SetWindowLongPtr(handle, WindowsAPI.GWL_EXSTYLE, new nint(originalExStyle));
				A_DetectHiddenWindows = oldDetectHiddenWindows;
			}
		}

		[Test, Category("Gui")]
		[Apartment(ApartmentState.STA)]
		public void NativeInputBox()
		{
			if (Script.IsHeadless)
				Assert.Ignore("InputBox requires an interactive desktop session.");

			const int probeMessage = 0xB035;
			var title = $"Keysharp InputBox probe {Guid.NewGuid():N}";
			nint dialogHandle = 0;
			string dialogClass = null;
			var managedPeer = true;
			Exception callbackFailure = null;
			var initHandler = new KeysharpFunc((Func<object, object, object, object, object>)((_, _, _, hwndArg) =>
			{
				var hwnd = unchecked((nint)hwndArg.Al());

				try
				{
					if (dialogHandle == 0)
					{
						dialogHandle = hwnd;
						dialogClass = WindowsAPI.GetClassName(hwnd);
						managedPeer = Control.FromHandle(hwnd) != null;

						//Posted so it arrives after InitializeDialog has populated the controls.
						if (!WindowsAPI.PostMessage(hwnd, probeMessage, 0, 0))
							throw new InvalidOperationException("Could not post the InputBox probe message.");
					}
				}
				catch (Exception ex)
				{
					callbackFailure ??= ex;
					_ = WindowsAPI.PostMessage(hwnd, WindowsAPI.WM_CLOSE, 0, 0);
				}

				return "";
			}));
			var probeHandler = new KeysharpFunc((Func<object, object, object, object, object>)((_, _, _, hwndArg) =>
			{
				var hwnd = unchecked((nint)hwndArg.Al());

				if (hwnd == dialogHandle)
				{
					try
					{
						var edit = WindowsAPI.GetDlgItem(hwnd, InputDialog.InputEditId);

						if (edit == 0 || !WindowsAPI.SetWindowText(edit, "typed value"))
							throw new InvalidOperationException("Could not update the native InputBox edit control.");
					}
					catch (Exception ex)
					{
						callbackFailure ??= ex;
					}
					finally
					{
						_ = WindowsAPI.PostMessage(hwnd, WindowsAPI.WM_COMMAND, (nint)InputDialog.OkId, 0);
					}
				}

				return "";
			}));

			_ = Keysharp.Builtins.Flow.OnMessage(WindowsAPI.WM_INITDIALOG, initHandler);
			_ = Keysharp.Builtins.Flow.OnMessage(probeMessage, probeHandler);

			try
			{
				//Safety net so a regression cannot leave a modal dialog waiting for a human.
				using var watchdog = new System.Threading.Timer(_ =>
				{
					var hwnd = WindowsAPI.FindWindow(null, title);

					if (hwnd != 0)
						_ = WindowsAPI.PostMessage(hwnd, WindowsAPI.WM_CLOSE, 0, 0);
				}, null, 2000, 100);
				var okResult = Dialogs.InputBox("Native InputBox probe", title, "", "initial value");
				var timeoutResult = Dialogs.InputBox("Native InputBox probe", title, "t0.05", "kept value");

				if (callbackFailure != null)
					Assert.Fail(callbackFailure.ToString());

				Assert.AreEqual("#32770", dialogClass, "the InputBox must be a native dialog whose messages reach OnMessage");
				Assert.IsFalse(managedPeer, "the InputBox must not be a decorated WinForms window");
				Assert.AreEqual("OK", InputBoxProperty(okResult, "Result"));
				Assert.AreEqual("typed value", InputBoxProperty(okResult, "Value"));
				Assert.AreEqual("Timeout", InputBoxProperty(timeoutResult, "Result"));
				Assert.AreEqual("kept value", InputBoxProperty(timeoutResult, "Value"));
			}
			finally
			{
				_ = Keysharp.Builtins.Flow.OnMessage(WindowsAPI.WM_INITDIALOG, initHandler, 0L);
				_ = Keysharp.Builtins.Flow.OnMessage(probeMessage, probeHandler, 0L);
			}
		}

		private static string InputBoxProperty(KeysharpObject result, string property)
			=> Script.GetPropertyValue(result, property).As();
#endif

#if LINUX
		[Test, Category("Gui")]
		public void GtkMenuOptions()
		{
			SkipIfUiInitializationBlocked("Test requires a live GTK application.");
			var callback = new KeysharpFunc((Func<object, object, object, object>)((_, _, _) => 0L));
			var menu = new Keysharp.Builtins.Menu();
			var menuBar = new Keysharp.Builtins.MenuBar();
			var first = (ToolStripMenuItem)menu.Add("First", callback);
			var second = (ToolStripMenuItem)menu.Add("Second", callback, "Radio BarBreak RTL");
			_ = menu.Check("Second");
			menu.MenuItem.Refresh();

			var nativeFirst = (Gtk.MenuItem)first.EtoItem.ControlObject;
			var nativeSecond = (Gtk.CheckMenuItem)second.EtoItem.ControlObject;
			var nativeMenu = (Gtk.Menu)menu.MenuItem.EtoMenu.ControlObject;
			using var firstColumn = nativeMenu.ChildGetProperty(nativeFirst, "left-attach");
			using var secondColumn = nativeMenu.ChildGetProperty(nativeSecond, "left-attach");

			Assert.IsTrue(nativeSecond.DrawAsRadio);
			Assert.AreEqual(Gtk.TextDirection.Rtl, nativeSecond.Direction);
			//The label carries the direction as well, because that is what right-aligns the item's text.
			Assert.AreEqual(Gtk.TextDirection.Rtl, nativeSecond.Child.Direction);
			Assert.AreEqual(Gtk.TextDirection.Ltr, nativeFirst.Child.Direction);
			//Columns must be adjacent: a spacer column would be as wide as a column of text.
			Assert.AreEqual(0U, Convert.ToUInt32(firstColumn.Val));
			Assert.AreEqual(1U, Convert.ToUInt32(secondColumn.Val));
			//The BarBreak divider is a border on the column's own items rather than a widget of its own.
			Assert.IsTrue(nativeSecond.StyleContext.HasClass(UnixMenuPresentation.BarBreakStyleClass));
			Assert.IsFalse(nativeFirst.StyleContext.HasClass(UnixMenuPresentation.BarBreakStyleClass));

			var right = (ToolStripMenuItem)menuBar.Add("Right", callback, "Right");
			var nativeRight = (Gtk.MenuItem)right.EtoItem.ControlObject;
			Assert.IsTrue((bool)nativeRight.GetType().GetProperty("RightJustified").GetValue(nativeRight));
		}
#endif

#if !WINDOWS
		[Test, Category("Gui")]
		public void MainWindowVisibility()
		{
			SkipIfUiInitializationBlocked("Test requires a live Eto Application (macOS testhost cannot drive AppKit).");
			var shown = false;
			using var mainWindow = new Keysharp.Internals.UI.Unix.MainWindow();
			mainWindow.Shown += (_, _) => shown = true;

			mainWindow.InitializeHidden();

			Assert.AreNotEqual(0, mainWindow.NativeHandle);
			Assert.IsFalse(mainWindow.Visible);
			Assert.IsFalse(shown);
		}

		[Test, Category("Gui")]
		public void EtoWindowStyleBits()
		{
			SkipIfUiInitializationBlocked("Test requires a live Eto Application (macOS testhost cannot drive AppKit).");

			using var form = new Eto.Forms.Form
			{
				WindowStyle = Eto.Forms.WindowStyle.Default,
				Closeable = true,
				Resizable = true,
				Minimizable = true,
				Maximizable = true
			};
			var style = EtoWindowStyles.ForWindow(form);

			// A decorated, fully-featured window carries the frame bits scripts test for. It is deliberately
			// NOT Eto's own WindowStyle enum value (Default is 0, None is 1), which collides meaninglessly
			// with the WS_* constants.
			Assert.AreEqual(EtoWindowStyles.WS_CAPTION, style & EtoWindowStyles.WS_CAPTION);
			Assert.AreEqual(EtoWindowStyles.WS_SYSMENU, style & EtoWindowStyles.WS_SYSMENU);
			Assert.AreEqual(EtoWindowStyles.WS_THICKFRAME, style & EtoWindowStyles.WS_THICKFRAME);
			Assert.AreEqual(EtoWindowStyles.WS_MINIMIZEBOX, style & EtoWindowStyles.WS_MINIMIZEBOX);
			Assert.AreEqual(EtoWindowStyles.WS_MAXIMIZEBOX, style & EtoWindowStyles.WS_MAXIMIZEBOX);
			Assert.AreEqual(0L, style & EtoWindowStyles.WS_POPUP, "a decorated window is not a popup");
			Assert.AreEqual(0L, style & EtoWindowStyles.WS_CHILD, "a top-level window is not a child");

			// Clearing the toolkit properties clears exactly the matching bits.
			form.Resizable = false;
			form.Minimizable = false;
			form.Maximizable = false;
			form.Closeable = false;
			style = EtoWindowStyles.ForWindow(form);
			Assert.AreEqual(0L, style & EtoWindowStyles.WS_THICKFRAME);
			Assert.AreEqual(0L, style & EtoWindowStyles.WS_MINIMIZEBOX);
			Assert.AreEqual(0L, style & EtoWindowStyles.WS_MAXIMIZEBOX);
			Assert.AreEqual(0L, style & EtoWindowStyles.WS_SYSMENU);

			// Borderless is the closest thing to a bare WS_POPUP, and drops the caption bits.
			form.WindowStyle = Eto.Forms.WindowStyle.None;
			style = EtoWindowStyles.ForWindow(form);
			Assert.AreEqual(EtoWindowStyles.WS_POPUP, style & EtoWindowStyles.WS_POPUP);
			Assert.AreEqual(0L, style & EtoWindowStyles.WS_CAPTION);

			// Child controls report WS_CHILD plus the visible/enabled state, and never frame bits.
			using var button = new Eto.Forms.Button { Enabled = false };
			var controlStyle = EtoWindowStyles.For(button);
			Assert.AreEqual(EtoWindowStyles.WS_CHILD, controlStyle & EtoWindowStyles.WS_CHILD);
			Assert.AreEqual(EtoWindowStyles.WS_DISABLED, controlStyle & EtoWindowStyles.WS_DISABLED);
			Assert.AreEqual(0L, controlStyle & EtoWindowStyles.WS_CAPTION);
		}
#endif

		[Test, Category("Gui")]
#if WINDOWS
		[Apartment(ApartmentState.STA)]
#endif
		public void ControlOptionSigns()
		{
			SkipIfUiInitializationBlocked("Parsing control options requires a constructed Gui.");
			var gui = new Gui(System.Array.Empty<object>());
			_ = gui.__New();

			try
			{
				// "In the absence of a preceding sign, a plus sign is assumed; for example, Wrap is the same as
				// +Wrap. By contrast, -Wrap would remove the word-wrapping property." An unrecognized token throws,
				// so merely reaching the assertion proves the signed spellings are accepted.
				Assert.AreEqual(Gui.GuiOptions.HorizontalAlignment.Center, gui.ParseOpt("text", "", "Center").halign);
				Assert.AreEqual(Gui.GuiOptions.HorizontalAlignment.Center, gui.ParseOpt("text", "", "+Center").halign);
				Assert.AreEqual(Gui.GuiOptions.HorizontalAlignment.Left, gui.ParseOpt("text", "", "-Center").halign);
				Assert.AreEqual(Gui.GuiOptions.VerticalAlignment.Middle, gui.ParseOpt("text", "", "+Middle").valign);

				Assert.AreEqual(true, gui.ParseOpt("edit", "", "Wrap").wordwrap);
				Assert.AreEqual(true, gui.ParseOpt("edit", "", "+Wrap").wordwrap);
				Assert.AreEqual(false, gui.ParseOpt("edit", "", "-Wrap").wordwrap);
				Assert.AreEqual(false, gui.ParseOpt("edit", "", "-Tabstop").tabstop);
				Assert.AreEqual(true, gui.ParseOpt("edit", "", "+ReadOnly").rdonly);
				// AHK compares "Multi" exactly, so a signed spelling has to survive but "Multiline" is not an option.
				Assert.AreEqual(true, gui.ParseOpt("edit", "", "xc+10 y+20 h400 w500 +Multi").multiline);

				// Hidden/Disabled additionally accept a trailing 1/0, which inverts the sign.
				Assert.AreEqual(false, gui.ParseOpt("edit", "", "Hidden").visible);
				Assert.AreEqual(true, gui.ParseOpt("edit", "", "Hidden0").visible);
				Assert.AreEqual(true, gui.ParseOpt("edit", "", "-Hidden").visible);
				Assert.AreEqual(false, gui.ParseOpt("edit", "", "+Disabled").enabled);
				Assert.AreEqual(true, gui.ParseOpt("edit", "", "Disabled0").enabled);

				Assert.AreEqual(1, gui.ParseOpt("checkbox", "", "Checked").ischecked);
				Assert.AreEqual(0, gui.ParseOpt("checkbox", "", "-Checked").ischecked);
				Assert.AreEqual(-1, gui.ParseOpt("checkbox", "", "Checked-1").ischecked);
				Assert.AreEqual(-1, gui.ParseOpt("checkbox", "", "CheckedGray").ischecked);

				// A word option must not be swallowed by a single-letter option that shares its first letter.
				var vscroll = gui.ParseOpt("listbox", "", "+VScroll");
				Assert.AreEqual(true, vscroll.vscroll);
				Assert.IsNull(vscroll.name, "VScroll must not be read as the name \"Scroll\"");
				Assert.AreEqual("MyEdit", gui.ParseOpt("edit", "", "vMyEdit").name);
				Assert.AreEqual(true, gui.ParseOpt("listview", "", "SortDesc").sortdesc);
				Assert.IsNull(gui.ParseOpt("listview", "", "SortDesc").sort, "SortDesc must not be read as Sort");

				// Icon selects the view for a ListView but the icon index for a Picture.
				Assert.AreEqual(View.SmallIcon, gui.ParseOpt("listview", "", "IconSmall").lvview);
				Assert.AreEqual(View.LargeIcon, gui.ParseOpt("listview", "", "Icon").lvview);
				Assert.AreEqual(1L, gui.ParseOpt("picture", "", "Icon2").iconnumber, "the 1-based icon index is stored 0-based");

				// The sign decides whether a raw style number is added or removed.
				Assert.AreEqual(0x40000, gui.ParseOpt("edit", "", "0x40000").addstyle);
				Assert.AreEqual(0x40000, gui.ParseOpt("edit", "", "+0x40000").addstyle);
				Assert.AreEqual(0x40000, gui.ParseOpt("edit", "", "-0x40000").remstyle);
				Assert.AreEqual(0x200, gui.ParseOpt("edit", "", "E0x200").addexstyle);
				Assert.AreEqual(0x200, gui.ParseOpt("edit", "", "-E0x200").remexstyle);
				// Styles accumulate rather than overwriting each other.
				Assert.AreEqual(0x40000 | 0x100, gui.ParseOpt("edit", "", "0x40000 0x100").addstyle);

				// Type-specific numeric options only apply to their own control type, and are matched by content
				// rather than by span identity, so they still work in the middle of an option string.
				Assert.IsTrue(gui.ParseOpt("datetime", "", "x0 1").dtopt1);
				Assert.IsTrue(gui.ParseOpt("monthcal", "", "x0 4").opt4);
				Assert.AreEqual(4, gui.ParseOpt("edit", "", "4").addstyle, "a bare number is a style for other types");
				// GuiControl.Opt() passes the type name as the script spelled it.
				Assert.IsTrue(gui.ParseOpt("DateTime", "", "2").dtopt2);

				// Applying an alignment must cope with control types that have no TextAlign at all: a Slider reads
				// halign as its tick placement instead, and a ProgressBar has no notion of alignment.
				var slider = (Gui.Control)gui.Add("Slider", "w100 +Center Page20 Line10 NoTicks AltSubmit");
				_ = gui.Add("Progress", "w100 +Center");
#if WINDOWS
				Assert.AreEqual(TickStyle.Both, ((TrackBar)slider.Ctrl).TickStyle, "+Center places a slider's ticks on both sides");
				// Edit-like controls spell TextAlign with the horizontal-only enum.
				Assert.AreEqual(System.Windows.Forms.HorizontalAlignment.Center, ((TextBox)((Gui.Control)gui.Add("Edit", "w100 +Center")).Ctrl).TextAlign);
				Assert.AreEqual(ContentAlignment.MiddleCenter, ((Label)((Gui.Control)gui.Add("Text", "w100 Center Middle")).Ctrl).TextAlign);
#endif

				// The sign is stripped before the font tokens are handed to ParseFont().
				var font = gui.ParseOpt("text", "", "+s12 +Bold").fontstyles;
				Assert.IsTrue(font.Contains("s12"), $"expected s12 in \"{font}\"");
				Assert.IsTrue(font.Contains("Bold"), $"expected Bold in \"{font}\"");
				Assert.IsFalse(font.Contains('+'), "font tokens must reach ParseFont() without their sign");
			}
			finally
			{
				_ = gui.Destroy();
			}
		}

		[Test, Category("Gui")]
#if WINDOWS
		[Apartment(ApartmentState.STA)]
#endif
		public void EmptyHotkeyControl()
		{
			SkipIfUiInitializationBlocked("Reading a Hotkey control's value requires a constructed Gui.");
			var gui = new Gui(System.Array.Empty<object>());
			_ = gui.__New();

			try
			{
				var hk = (Gui.Control)gui.Add("Hotkey", "w160");

				// AHK reads the control with HKM_GETHOTKEY, which yields 0 while nothing is set; HotkeyToText()
				// then produces "" because VK 0 has no key name. The control displays "None", but that string
				// must never reach the script - callers test the value against "" to detect "no hotkey".
				Assert.AreEqual("", hk.Value, "an empty Hotkey control's value must be blank, not \"None\"");
				Assert.AreEqual("", hk.Text, "an empty Hotkey control's text must be blank, not \"None\"");

				hk.Value = "^!a";
				Assert.AreEqual("^!A", hk.Value);

				// Assigning "" clears the control, and it reads back blank again rather than round-tripping
				// the "None" it now displays.
				hk.Value = "";
				Assert.AreEqual("", hk.Value);

				// AHK's TextToHotkey() maps any unparseable key name - including the displayed "None" - to 0,
				// so assigning "None" clears the control too.
				hk.Value = "^!a";
				hk.Value = "None";
				Assert.AreEqual("", hk.Value);
			}
			finally
			{
				_ = gui.Destroy();
			}
		}

		[Test, Category("Gui")]
#if WINDOWS
		[Apartment(ApartmentState.STA)]
#endif
		public void FileSelect()
		{
			if (Script.IsHeadless)
				Assert.Ignore("FileSelect requires an interactive desktop session.");

			var fullpath = Path.GetFullPath(string.Concat(path, "DirCopy/file1.txt"));
			var files = Dialogs.FileSelect();
			//MsgBox(files);
			files = Dialogs.FileSelect("", fullpath, "Filename - Path and file", "");
			fullpath = Path.GetFullPath(string.Concat(path, "DirCopy/"));
			files = Dialogs.FileSelect("M", fullpath, "Filename - Path only - Multiselect", "");
			//MsgBox(files);
			fullpath = Path.GetFullPath(string.Concat(path, "DirCopy/file1.txt"));
			files = Dialogs.FileSelect("S16", fullpath, "Filename - Path and file - Text files filter - Save & prompt for overwrite", "Text files |*.txt;*.wri;*.ini");
			fullpath = Path.GetFullPath(string.Concat(path, "DirCopy/"));
			files = Dialogs.FileSelect("S16", fullpath, "Filename - Path only - Text files filter - Save & prompt for overwrite", "Text files |*.txt;*.wri;*.ini");
			//MsgBox(files);
			files = Dialogs.FileSelect("D", "D:\\", "", "");
		}

		[Test, Category("Gui")]
#if WINDOWS
		// Same STA apartment as Theme so the two share a per-test STA thread that is torn down afterward. Otherwise this
		// test's message loop strands a SystemEvents (dark-mode/theming) subscription on the persistent runner thread,
		// and Theme's Application.SetColorMode later deadlocks marshaling a synchronous notification back to it.
		[Apartment(ApartmentState.STA)]
#endif
		public void MsgBox()
		{
			if (Script.IsHeadless)
				Assert.Ignore("MsgBox requires an interactive desktop session.");
#if LINUX
			if (!Keysharp.Internals.Platform.Desktop.IsX11Available)
				Assert.Ignore("Linux MsgBox automation currently requires X11-backed Eto windows.");
#endif

			var (cts, task) = StartMsgBoxAutoAccept();
			KeysharpForm form = null;
			try
			{
				form = CreateMsgBoxHostForm();
				form.Shown += Form_Shown;
				RunMsgBoxHost(form);
			}
			finally
			{
				form?.Close();
				cts.Cancel();
				task.Wait();
			}

			var timeoutForm = CreateMsgBoxHostForm();
			timeoutForm.Shown += TimeoutForm_Shown;
			RunMsgBoxHost(timeoutForm);
		}

		[Test, Category("Gui")]
#if WINDOWS
		[Apartment(ApartmentState.STA)]
#endif
		public void Theme()
		{
#if WINDOWS
			var originalTheme = Ks.A_GuiTheme.ToString();

			try
			{
				Ks.A_GuiTheme = "Dark";
				Assert.AreEqual("Dark", Ks.A_GuiTheme);

				Ks.A_GuiTheme = "System";
				Assert.AreEqual("System", Ks.A_GuiTheme);

				Ks.A_GuiTheme = "Classic";
				Assert.AreEqual("Classic", Ks.A_GuiTheme);
			}
			finally
			{
				Ks.A_GuiTheme = originalTheme;
			}
#else
			var originalTheme = Ks.A_GuiTheme.ToString();

			try
			{
				Ks.A_GuiTheme = "Dark";
				Assert.AreEqual("Dark", Ks.A_GuiTheme);
			}
			finally
			{
				Ks.A_GuiTheme = originalTheme;
			}
#endif
		}

		private void Form_Shown(object sender, EventArgs e)
		{
			var ret = Dialogs.MsgBox("ok, hand, def: 1", MsgBoxTitle, "0 16");
			Assert.AreEqual(ret.ToUpper(), "OK");
			ret = Dialogs.MsgBox("ok, hand, def: 1", MsgBoxTitle, 16);
			Assert.AreEqual(ret.ToUpper(), "OK");
			ret = Dialogs.MsgBox("ok-cancel, question, def: 2", MsgBoxTitle, "1 32 256");
			Assert.AreEqual(ret, "Cancel");
			ret = Dialogs.MsgBox("ok-cancel, question, def: 2", MsgBoxTitle, 1 | 32 | 256);
			Assert.AreEqual(ret, "Cancel");
			ret = Dialogs.MsgBox("yes-no-cancel, asterisk/info, def: 1", MsgBoxTitle, "3 64");
			Assert.AreEqual(ret, "Yes");
			ret = Dialogs.MsgBox("yes-no-cancel, asterisk/info, def: 1", MsgBoxTitle, 3 | 64);
			Assert.AreEqual(ret, "Yes");
			ret = Dialogs.MsgBox("yes-no, asterisk/info, def: 2", MsgBoxTitle, "4 64 256");
			Assert.AreEqual(ret, "No");
			ret = Dialogs.MsgBox("yes-no, asterisk/info, def: 2", MsgBoxTitle, 4 | 64 | 256);
			Assert.AreEqual(ret, "No");
#if WINDOWS
			ret = Dialogs.MsgBox("abort-retry-ignore, exclamation, def: 3, just: right", MsgBoxTitle, "2 48 512 524288");
			Assert.AreEqual(ret, "Ignore");
			ret = Dialogs.MsgBox("abort-retry-ignore, exclamation, def: 3, just: right", MsgBoxTitle, 2 | 48 | 512 | 524288);
			Assert.AreEqual(ret, "Ignore");
			ret = Dialogs.MsgBox("retry-cancel, asterisk/info, def: 1", MsgBoxTitle, "5 64");
			Assert.AreEqual(ret, "Retry");
			ret = Dialogs.MsgBox("retry-cancel, asterisk/info, def: 1", MsgBoxTitle, 5 | 64);
			Assert.AreEqual(ret, "Retry");
			ret = Dialogs.MsgBox("cancel-try-continue, exclamation, def: 1", MsgBoxTitle, "0x36");
			Assert.AreEqual(ret, "Cancel");
#endif
			(sender as Form)?.Close();
		}

		private static void TimeoutForm_Shown(object sender, EventArgs e)
		{
			var ret = Dialogs.MsgBox("ok, hand, def: 1, timeout: 0.2", MsgBoxTitle, "0 16 t0.2");
			Assert.AreEqual("Timeout", ret);
			(sender as Form)?.Close();
		}

		private static KeysharpForm CreateMsgBoxHostForm()
		{
#if WINDOWS
			return new KeysharpForm
			{
				Size = new System.Drawing.Size(500, 500),
				StartPosition = FormStartPosition.CenterScreen,
				Text = "MessageBox holder",
			};
#else
			return new KeysharpForm
			{
				Title = "MessageBox holder",
				Size = new Size(500, 500),
			};
#endif
		}

		private static void RunMsgBoxHost(KeysharpForm form)
		{
#if WINDOWS
			Application.Run(form);
#else
			var app = Application.Instance ?? new Application();
			app.MainForm = form;
			form.Show();

			while (form.Visible)
				app.RunIteration();

			if (ReferenceEquals(app.MainForm, form))
				app.MainForm = null;
#endif
		}

		private static (CancellationTokenSource cts, Task task) StartMsgBoxAutoAccept()
		{
			var cts = new CancellationTokenSource();

#if WINDOWS
			var task = Task.Run(() =>
			{
				while (!cts.IsCancellationRequested)
				{
					var wnd = WindowsAPI.FindWindow(null, MsgBoxTitle);

					if (wnd != 0)
					{
						_ = WindowsAPI.SetForegroundWindow(wnd);
						SendKeys.SendWait(" ");
						Thread.Sleep(100);
					}
					else
						Thread.Sleep(50);
				}
			});
#else
			var responses = new ConcurrentQueue<string>(new[] { "OK", "OK", "Cancel", "Cancel", "Yes", "Yes", "No", "No" });
			var task = Task.Run(() =>
			{
				while (!cts.IsCancellationRequested)
				{
					if (responses.TryPeek(out var response) && TryAcceptActiveMessageBox(response))
						_ = responses.TryDequeue(out _);

					Thread.Sleep(50);
				}
			});
#endif

			return (cts, task);
		}

#if !WINDOWS
		private static bool TryAcceptActiveMessageBox(string expectedResult)
		{
#if LINUX
			var accepted = false;
			Application.Instance.Invoke(() =>
			{
				foreach (var window in Gtk.Window.ListToplevels())
				{
					if (window is not Gtk.MessageDialog dialog || !dialog.Visible || dialog.Title != MsgBoxTitle)
						continue;

					dialog.Respond(expectedResult switch
					{
						"OK" => (int)Gtk.ResponseType.Ok,
						"Cancel" => (int)Gtk.ResponseType.Cancel,
						"Yes" => (int)Gtk.ResponseType.Yes,
						"No" => (int)Gtk.ResponseType.No,
						_ => (int)Gtk.ResponseType.None
					});
					accepted = true;
					break;
				}
			});
			return accepted;
#elif OSX
			var accepted = false;
			Application.Instance.Invoke(() =>
			{
				foreach (var window in AppKit.NSApplication.SharedApplication.Windows)
				{
					if (window?.IsVisible != true || window.Title != MsgBoxTitle)
						continue;

					if (TryClickMacButton(window, expectedResult))
					{
						accepted = true;
						break;
					}
				}
			});
			return accepted;
#else
			return false;
#endif
		}

#if OSX
		private static bool TryClickMacButton(AppKit.NSWindow window, string expectedResult)
		{
			return window.ContentView != null && TryClickMacButton(window.ContentView, expectedResult);
		}

		private static bool TryClickMacButton(AppKit.NSView view, string expectedResult)
		{
			if (view is AppKit.NSButton button && string.Equals(button.Title, expectedResult, StringComparison.OrdinalIgnoreCase))
			{
				button.PerformClick(null);
				return true;
			}

			foreach (var subview in view.Subviews)
			{
				if (TryClickMacButton(subview, expectedResult))
					return true;
			}

			return false;
		}
#endif
#endif
	}
}
