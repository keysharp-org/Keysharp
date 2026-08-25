using Assert = NUnit.Framework.Legacy.ClassicAssert;
using Keysharp.Internals;
using Keysharp.Internals.Images;

namespace Keysharp.Tests
{
	/// <summary>
	/// The window-icon loader and the Taskbar surface. Everything here is headless: no window is shown, so these
	/// stay in the curated set. What a shell actually draws cannot be asserted from a test and is not attempted.
	/// </summary>
	[Category("Internal")]
	public class IconInternals : TestRunner
	{
		private static string Asset(string name)
			=> Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "assets", name));

		/// <summary>
		/// The point of LoadIconSet over LoadImage: a multi-size .ico keeps all of its frames, so the toolkit can
		/// pick one per slot instead of scaling a single frame into both. LoadImage is asserted alongside it,
		/// because "the loader we already had would have done" is the claim this whole path rests on.
		/// </summary>
		[Test, Category("Gui"), Category("Curated")]
		public void IconSetKeepsEveryFrameWhereLoadImageDoesNot()
		{
			using var icon = ImageHelper.LoadIconSet(Asset("Keysharp.ico"), 0L);
			Assert.IsNotNull(icon);
			Assert.Greater(FrameCount(icon), 1, "a multi-size .ico must not be collapsed to one frame");
			var (bmp, single) = ImageHelper.LoadImage(Asset("Keysharp.ico"), 0, 0, 0L);
			using (bmp)
				Assert.AreEqual(1, FrameCount(single as Icon), "LoadImage collapses; that is why LoadIconSet exists");
		}

		/// <summary>
		/// The module path extracts through PrivateExtractIcons and then destroys the handle it was given, so the
		/// icon that comes back has to be a copy which still works afterwards.
		/// </summary>
		[Test, Category("Gui"), Category("Curated")]
		public void ModuleIconSurvivesTheHandleItWasExtractedFrom()
		{
#if WINDOWS
			using var icon = ImageHelper.LoadIconSet(@"C:\Windows\System32\shell32.dll", 173L, 32);
			Assert.IsNotNull(icon);
			Assert.AreEqual(32, icon.Width);
			using var bmp = icon.ToBitmap();//Would fail on a destroyed handle.
			Assert.AreEqual(32, bmp.Width);
#else
			Assert.Ignore("Addressing a module's icons by index is a Windows facility.");
#endif
		}

		/// <summary>
		/// The requested size reaches the icon, which is what decides the large icon a window shows. A source
		/// carrying fixed sizes supplies the nearest one it has -- Keysharp.ico has a 48 but no 40 -- while one
		/// that is resampled anyway lands exactly.
		/// </summary>
		[Test, Category("Gui"), Category("Curated")]
		public void IconSetLoadsAtTheRequestedSize()
		{
			using var unsized = ImageHelper.LoadIconSet(Asset("Keysharp.ico"), 0L);
			using var sized = ImageHelper.LoadIconSet(Asset("Keysharp.ico"), 0L, 48);
			Assert.IsNotNull(sized);
			Assert.AreEqual(48, sized.Width, "a size the file carries is the size given");
			Assert.AreNotEqual(unsized.Width, sized.Width, "and it is the request that changed it, not the default");
			using var png = ImageHelper.LoadIconSet(Asset("Keysharp.png"), 0L, 40);
			Assert.IsNotNull(png);
			Assert.AreEqual(40, png.Width, "a resampled source lands exactly on the request");
		}

		/// <summary>
		/// A bad source answers null rather than throwing, so the callers can raise a ValueError naming the source
		/// instead of letting a decoder's own exception type escape.
		/// </summary>
		[Test, Category("Gui"), Category("Curated")]
		public void IconSetReturnsNullForABadSource()
		{
			Assert.IsNull(ImageHelper.LoadIconSet(Path.Combine(Path.GetTempPath(), "keysharp-no-such-icon.ico"), 0L));
			Assert.IsNull(ImageHelper.LoadIconSet("", 0L));
		}

		/// <summary>
		/// The state vocabulary belongs to the script, not to whatever the internal enum members happen to be
		/// called, so a number or a comma list must be refused rather than quietly accepted.
		/// </summary>
		[Test, Category("Gui"), Category("Curated")]
		public void TaskbarRejectsAnUnknownProgressState()
		{
			//A handle that names no window is fine here: nothing is drawn, because the state never validates.
			var bar = new Ks.KeysharpTaskbar([1L]);
			_ = Assert.Throws<Keysharp.Builtins.KeysharpException>(() => bar.SetProgressState("8"));
			_ = Assert.Throws<Keysharp.Builtins.KeysharpException>(() => bar.SetProgressState("Normal,Error"));
			_ = Assert.Throws<Keysharp.Builtins.KeysharpException>(() => bar.SetProgressState(""));
		}

		/// <summary>
		/// The flagship example on the Gui.Icon page: an icon copied from one window to another. It only works
		/// because the getter hands back an Image the setter accepts, which is the whole reason for that type.
		/// </summary>
		[Test, Category("Gui"), Category("Curated")]
		[Apartment(ApartmentState.STA)]
		public void GuiIconRoundTripsThroughImage()
		{
			var first = new Gui(System.Array.Empty<object>());
			_ = first.__New();
			var second = new Gui(System.Array.Empty<object>());
			_ = second.__New();

			try
			{
				_ = first.SetIcon(Asset("Keysharp_s.ico"), null, "w48");
				Assert.IsInstanceOf<Ks.KeysharpImage>(first.Icon, "the getter answers with an Image, not a handle");
				second.Icon = first.Icon;
				Assert.IsInstanceOf<Ks.KeysharpImage>(second.Icon, "which the setter then accepts");
				//"*" goes back to the shared script icon, which the window must not then treat as its own.
				_ = first.SetIcon("*");
				Assert.AreSame(Script.TheScript.scriptIcon, first.form.Icon);
			}
			finally
			{
				_ = first.Destroy();
				_ = second.Destroy();
			}

			//The window that went back to the script icon has just been destroyed. If it had taken ownership of
			//that shared icon, destroying it would have disposed the one the tray and every other window use.
			using var stillGood = Script.TheScript.scriptIcon.ToBitmap();
			Assert.Greater(stillGood.Width, 0, "Destroy must not have disposed the shared script icon");
		}

		/// <summary>
		/// A source that cannot be read is a ValueError naming it, not whatever the decoder happened to throw.
		/// </summary>
		[Test, Category("Gui"), Category("Curated")]
		[Apartment(ApartmentState.STA)]
		public void GuiSetIconRejectsABadSource()
		{
			var gui = new Gui(System.Array.Empty<object>());
			_ = gui.__New();

			try
			{
				var missing = Path.Combine(Path.GetTempPath(), "keysharp-no-such-icon.ico");
				var ex = Assert.Throws<Keysharp.Builtins.KeysharpException>(() => gui.SetIcon(missing));
				Assert.IsTrue(ex.Message.Contains("keysharp-no-such-icon"), "the error has to name the source");
			}
			finally
			{
				_ = gui.Destroy();
			}
		}

		/// <summary>
		/// A Taskbar with no window to decorate is a programming mistake, not a silent no-op.
		/// </summary>
		[Test, Category("Gui"), Category("Curated")]
		public void TaskbarRejectsAnEmptyHandle()
		{
			_ = Assert.Throws<Keysharp.Builtins.KeysharpException>(() => new Ks.KeysharpTaskbar([0L]));
		}

		/// <summary>
		/// The same surface as a script sees it. Everything above calls C# directly, which skips the binder --
		/// and Taskbar is the first class here to give one script name both a class member and an instance
		/// member, so that dispatch is worth exercising for real rather than reasoning about.
		/// </summary>
		[Test, Category("Gui"), Category("Curated"), NonParallelizable]
		public void ScriptSurfaceBinds()
		{
			SkipIfUiInitializationBlocked("Creating an AppKit window requires OS thread 1.");
			Assert.IsTrue(TestScript("gui-icon-taskbar", false));
		}

		/// <summary>
		/// #NoTrayIcon suppresses the tray icon, not the icon: as in AutoHotkey the file is still loaded and still
		/// becomes the script's, which is what a GUI window created afterwards wears.
		/// </summary>
		[Test, Category("Gui"), Category("Curated"), NonParallelizable]
		public void TraySetIconAppliesUnderNoTrayIcon()
		{
			var script = Script.TheScript;
			var noTray = script.NoTrayIcon;
			var previousFile = Accessors.A_IconFile;
			var previousNumber = Accessors.A_IconNumber;
			var previousIcon = script.customIcon;

			try
			{
				script.NoTrayIcon = true;
				_ = ToolTips.TraySetIcon(Asset("Keysharp_s.ico"));
				Assert.IsNotNull(script.customIcon, "the icon is loaded even when the tray is suppressed");
				Assert.AreSame(script.customIcon, script.scriptIcon);
				Assert.AreEqual(Asset("Keysharp_s.ico"), Accessors.A_IconFile);
				Assert.AreEqual(1L, Accessors.A_IconNumber, "an omitted icon number reads back as 1, not unset");
				_ = ToolTips.TraySetIcon("*");
				Assert.IsNull(script.customIcon, "'*' goes back to the default icon");
			}
			finally
			{
				script.customIcon = previousIcon;
				Accessors.A_IconFile = previousFile;
				Accessors.A_IconNumber = previousNumber;
				script.NoTrayIcon = noTray;
			}
		}

		private static int FrameCount(Icon icon)
		{
#if WINDOWS
			using var ms = new MemoryStream();
			icon.Save(ms);
			return BitConverter.ToInt16(ms.ToArray(), 4);
#else
			return icon.Frames.Count();
#endif
		}
	}
}
