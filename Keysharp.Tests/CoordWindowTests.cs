namespace Keysharp.Tests;

[TestFixture]
public class CoordWindowTests : TestRunner
{
	[Test, Category("Internal"), Category("Curated")]
	public void CoordinateOrigin()
	{
#if WINDOWS
		var nativeWindow = new WindowsWindow();
		Assert.IsTrue(nativeWindow.TryGetBounds(-1, false, out var missingFrame));
		Assert.That(missingFrame, Is.EqualTo(Rectangle.Empty));
		Assert.IsTrue(nativeWindow.TryGetBounds(-1, true, out var missingClient));
		Assert.That(missingClient, Is.EqualTo(Rectangle.Empty));
		Assert.IsFalse(nativeWindow.TryGetCoordBounds(-1, false, out _));
		Assert.IsFalse(nativeWindow.TryGetCoordBounds(-1, true, out _));
#endif
		var window = new FakeWindow { Origin = new Point(300, 200) };
		var mouse = new FakeMouse(Platform.Mouse);
		using var platform = new PlatformSwap(window, mouse: mouse);
		using var guarded = Keysharp.Runtime.Flow.EnterTry();
		using var scope = new Ks.CoordWindow(42L);

		Assert.That(Convert(5, -6), Is.EqualTo((305, 194)));
		Assert.That((window.Handle, window.Client), Is.EqualTo(((nint)42, true)));
#if WINDOWS
		Assert.That(MouseDeltaTests.PlaybackOrigin(scope), Is.EqualTo("310,220|None"));
#endif
		window.Origin = new Point(-7, 12);
		Assert.That(Convert(5, -6), Is.EqualTo((-2, 6)));

		Assert.That(ThreadAccessors.SetCoordMode(CoordMode.Pixel, CoordModeType.Window), Is.EqualTo(CoordModeType.Client));
		Assert.That(Convert(5, -6), Is.EqualTo((295, 164)));
		Assert.IsFalse(window.Client);
		ThreadAccessors.SetCoordMode(CoordMode.Pixel, CoordModeType.Client);

		window.Exists = false;
		Assert.IsInstanceOf<TargetError>(Raised());
		Assert.That(Current, Is.SameAs(scope));
		window.Exists = true;
		window.Minimized = true;
		Assert.IsInstanceOf<TargetError>(Raised());

		// Construction selects the window; geometry is required only when coordinates are converted.
		using (var minimized = new Ks.CoordWindow(99L))
		{
			Assert.That(Current, Is.SameAs(minimized));
			Assert.IsInstanceOf<TargetError>(Raised());
		}

		window.Minimized = false;
		window.GeometryAvailable = false;
		Assert.IsInstanceOf<UnsupportedError>(Raised());
#if WINDOWS
		Assert.That(MouseDeltaTests.PlaybackOrigin(scope), Is.EqualTo("NoOp:777,-55|Geometry"));
#endif
		window.GeometryAvailable = true;
		Assert.That(Convert(5, -6), Is.EqualTo((-2, 6)));

		window.CloseDuringBounds = true;
		Assert.IsInstanceOf<TargetError>(Raised());
		Assert.IsFalse(window.Exists);
		window.Exists = true;
#if WINDOWS
		Assert.That(MouseDeltaTests.PlaybackOrigin(scope), Is.EqualTo("NoOp:777,-55|Target"));
		mouse.CursorAvailable = false;
		Assert.That(MouseDeltaTests.PlaybackOrigin(scope), Is.EqualTo("NoOp|Target"));
		mouse.CursorAvailable = true;
#endif
		window.CloseDuringBounds = false;
		window.Exists = true;

		ThreadAccessors.SetCoordMode(CoordMode.Pixel, CoordModeType.Screen);
		window.Exists = false;
		var reads = window.Reads;
		Assert.That(Convert(5, -6), Is.EqualTo((5, -6)));
		Assert.That(window.Reads, Is.EqualTo(reads));
		Assert.That(Current, Is.SameAs(scope));

		scope.__Delete();
		window.Exists = true;
		Assert.That(Convert(5, -6), Is.EqualTo((15, 14)));
		Assert.That(window.Handle, Is.EqualTo((nint)7));
#if WINDOWS
		ThreadAccessors.SetCoordMode(CoordMode.Mouse, CoordModeType.Screen);
		var activeWindowReads = window.ActiveWindowReads;
		Assert.That(MouseDeltaTests.PlaybackOrigin(null), Is.EqualTo("10,20|None"));
		Assert.That(window.ActiveWindowReads, Is.EqualTo(activeWindowReads));
#endif
	}

	[Test, Category("Internal"), Category("Curated")]
	public void PixelOrigin()
	{
		var window = new FakeWindow { Origin = new Point(300, 200) };
		var screen = new FakeScreen(Platform.Screen);
		using var platform = new PlatformSwap(window, screen);
		using var guarded = Keysharp.Runtime.Flow.EnterTry();
		using var scope = new Ks.CoordWindow(42L);

		Assert.That(Keysharp.Builtins.Screen.PixelGetColor(5L, -6L), Is.EqualTo("0x123456"));
		Assert.That(screen.Pixels, Is.EqualTo(new[] { (305, 194) }));

		window.Minimized = true;
		Assert.IsInstanceOf<TargetError>(Assert.Throws<KeysharpException>(() => Keysharp.Builtins.Screen.PixelGetColor(5L, -6L)).UserError);
		var foundX = new VarRef("kept X");
		var foundY = new VarRef("kept Y");
		Assert.IsInstanceOf<TargetError>(Assert.Throws<KeysharpException>(() =>
			Keysharp.Builtins.Screen.PixelSearch(foundX, foundY, 0L, 0L, 1L, 1L, 0L)).UserError);
		Assert.That((foundX.__Value, foundY.__Value), Is.EqualTo(((object)"kept X", (object)"kept Y")));
		Assert.That(screen.Pixels.Count, Is.EqualTo(1));

		var reports = 0;
		_ = Errors.OnError(new KeysharpFunc((Func<object, object, object>)((_, _) => { reports++; return -1L; })));

		using (Keysharp.Runtime.Flow.EnterUnguarded())
		{
			int x = 5, y = -6;
			Assert.IsFalse(WindowCoords.CoordToScreen(ref x, ref y, CoordMode.Pixel));
			Assert.That((x, y), Is.EqualTo((5, -6)));
			_ = Keysharp.Builtins.Screen.PixelGetColor(5L, -6L);
			_ = Keysharp.Builtins.Screen.PixelSearch(foundX, foundY, 0L, 0L, 1L, 1L, 0L);
		}

		Assert.That(reports, Is.EqualTo(3));
		Assert.That((foundX.__Value, foundY.__Value), Is.EqualTo(((object)"kept X", (object)"kept Y")));
		Assert.That(screen.Pixels.Count, Is.EqualTo(1));
		Assert.That(screen.Captures, Is.Zero);
	}

	[Test, Category("Internal"), Category("Curated")]
	public void CurrentLifetime()
	{
		var window = new FakeWindow();
		using var platform = new PlatformSwap(window);
		_ = UseQueuedMainContext();
		using var outer = new Ks.CoordWindow(1L);
		ThreadAccessors.SetCoordMode(CoordMode.Mouse, CoordModeType.Window);

		var inner = HoldCurrentAcrossCollection();
		CollectScopes();

		Assert.IsFalse(inner.TryGetTarget(out _));
		Assert.That(Current, Is.SameAs(outer));
		Assert.That(ThreadAccessors.GetCoordMode(CoordMode.Mouse), Is.EqualTo(CoordModeType.Window));
		Assert.IsTrue(s.DestructorPump.RunPendingDestructors());
		Assert.That(Current, Is.SameAs(outer));

		var abandoned = AbandonBelowCurrent(out var retained);

		using (retained)
		{
			CollectScopes();
			Assert.IsFalse(abandoned.TryGetTarget(out _));

			using (var newest = new Ks.CoordWindow(4L))
			{
				Assert.That(Current, Is.SameAs(newest));
				Assert.That(s.Threads.CurrentThread.coordWindows.Count, Is.EqualTo(3));
			}

			Assert.That(Current, Is.SameAs(retained));
		}

		Assert.That(Current, Is.SameAs(outer));
	}

	[Test, Category("Internal"), Category("Curated")]
	public void ReleaseFromAnotherRealThread()
	{
		using var platform = new PlatformSwap(new FakeWindow());
		var original = ThreadAccessors.GetCoordMode(CoordMode.Pixel);
		using var outer = new Ks.CoordWindow(1L);
		ThreadAccessors.SetCoordMode(CoordMode.Pixel, CoordModeType.Window);
		using var inner = new Ks.CoordWindow(2L);
		ThreadAccessors.SetCoordMode(CoordMode.Pixel, CoordModeType.Screen);

		System.Threading.Tasks.Task.Run(() => outer.__Delete()).GetAwaiter().GetResult();

		Assert.That(Current, Is.SameAs(inner));
		Assert.That(ThreadAccessors.GetCoordMode(CoordMode.Pixel), Is.EqualTo(CoordModeType.Screen));
		inner.__Delete();
		Assert.That(Current, Is.EqualTo(Script.DefaultObject));
		Assert.That(ThreadAccessors.GetCoordMode(CoordMode.Pixel), Is.EqualTo(original));
	}

	[Test, Category("Internal"), Category("Curated")]
	public void ThreadStateReuse()
	{
		using var platform = new PlatformSwap(new FakeWindow());
		var threads = s.Threads;
		using var outer = new Ks.CoordWindow(1L);
		ThreadAccessors.SetCoordMode(CoordMode.Caret, CoordModeType.Screen);

		Assert.IsTrue(threads.TryBeginThread(out var launched));
		Ks.CoordWindow retained = null;

		try
		{
			Assert.IsNull(launched.coordWindows);
			Assert.That(Current, Is.EqualTo(Script.DefaultObject));
			Assert.That(ThreadAccessors.GetCoordMode(CoordMode.Caret), Is.EqualTo(CoordModeType.Client));
			retained = new Ks.CoordWindow(2L);
			ThreadAccessors.SetCoordMode(CoordMode.Caret, CoordModeType.Window);
		}
		finally
		{
			threads.EndThread(launched);
		}

		Assert.IsNull(launched.coordWindows);
		Assert.That(retained.Hwnd, Is.EqualTo(2L));
		Assert.That(Current, Is.SameAs(outer));
		Assert.That(ThreadAccessors.GetCoordMode(CoordMode.Caret), Is.EqualTo(CoordModeType.Screen));
		Assert.IsTrue(threads.TryBeginThread(out var reused));

		try
		{
			Assert.That(reused, Is.SameAs(launched));
			Assert.IsNull(reused.coordWindows);
			using var replacement = new Ks.CoordWindow(3L);
			retained.__Delete();
			Assert.That(Current, Is.SameAs(replacement));
			Assert.That(ThreadAccessors.GetCoordMode(CoordMode.Caret), Is.EqualTo(CoordModeType.Client));
		}
		finally
		{
			threads.EndThread(reused);
		}

		Assert.That(Current, Is.SameAs(outer));
		Assert.That(ThreadAccessors.GetCoordMode(CoordMode.Caret), Is.EqualTo(CoordModeType.Screen));
	}

	private static object Current => Ks.CoordWindow.staticget_Current(null);

	private static (int X, int Y) Convert(int x, int y)
	{
		Assert.IsTrue(WindowCoords.CoordToScreen(ref x, ref y, CoordMode.Pixel));
		return (x, y);
	}

	private static Error Raised()
		=> Assert.Throws<KeysharpException>(() => Convert(5, -6)).UserError;

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static Ks.CoordWindow CreateCurrent()
	{
		var created = new Ks.CoordWindow(2L);
		var current = (Ks.CoordWindow)Current;
		Assert.That(current, Is.SameAs(created));
		return current;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static WeakReference<Ks.CoordWindow> HoldCurrentAcrossCollection()
	{
		var held = CreateCurrent();
		var weak = new WeakReference<Ks.CoordWindow>(held);
		CollectScopes();
		Assert.IsTrue(weak.TryGetTarget(out _));
		Assert.That(Current, Is.SameAs(held));
		GC.KeepAlive(held);
		return weak;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static WeakReference<Ks.CoordWindow> AbandonBelowCurrent(out Ks.CoordWindow retained)
	{
		var abandoned = CreateCurrent();
		retained = new Ks.CoordWindow(3L);
		return new WeakReference<Ks.CoordWindow>(abandoned);
	}

	private static void CollectScopes()
	{
		GC.Collect();
		GC.WaitForPendingFinalizers();
		GC.Collect();
	}

	private sealed class PlatformSwap : IDisposable
	{
		private static readonly FieldInfo host = typeof(Platform).GetField("host", BindingFlags.Static | BindingFlags.NonPublic);
		private readonly object previous;

		internal PlatformSwap(IWindow window, IScreen screen = null, IMouse mouse = null)
		{
			var resolved = Platform.Instance;
			previous = host.GetValue(null);
			host.SetValue(null, new Lazy<PlatformHost>(new Host(resolved, window, screen ?? resolved.Screen, mouse ?? resolved.Mouse)));
		}

		public void Dispose() => host.SetValue(null, previous);
	}

	private sealed class Host(PlatformHost inner, IWindow window, IScreen screen, IMouse mouse) : PlatformHost
	{
		private readonly IPermissionManager permissions = new DefaultPermissionManager();

		internal override IWindow Window => window;
		internal override ControlManagerBase Control => inner.Control;
		internal override IScreen Screen => screen;
		internal override IMonitorControl MonitorControl => inner.MonitorControl;
		internal override IOverlay Overlay => inner.Overlay;
		internal override IMouse Mouse => mouse;
		internal override IKeyboard Keyboard => inner.Keyboard;
		internal override IHotkeys Hotkeys => inner.Hotkeys;
		internal override ISession Session => inner.Session;
		internal override IPermissionManager Permissions => permissions;
		internal override IClipboard GetClipboardCore(Script owner) => inner.GetClipboardCore(owner);
	}

	private sealed class FakeMouse(IMouse inner) : IMouse
	{
		internal bool CursorAvailable = true;

		public bool TryGetCursorPos(out int x, out int y)
		{
			x = 777;
			y = -55;
			return CursorAvailable;
		}

		public string GetCursorShape() => inner.GetCursorShape();
		public void SetCursorShape(string name) => inner.SetCursorShape(name);
		public bool SupportsCursorQueryAndMove => inner.SupportsCursorQueryAndMove;
		public bool TryMoveAbsolute(int x, int y) => inner.TryMoveAbsolute(x, y);
		public bool TryGetButtonStateLogical(uint vk, out bool down) => inner.TryGetButtonStateLogical(vk, out down);
		public bool TryGetButtonStatePhysical(uint vk, out bool down) => inner.TryGetButtonStatePhysical(vk, out down);
	}

	private sealed class FakeWindow : WindowBase
	{
		internal bool Exists = true;
		internal bool Minimized;
		internal bool GeometryAvailable = true;
		internal bool CloseDuringBounds;
		internal Point Origin;
		internal int Reads;
		internal int ActiveWindowReads;
		internal nint Handle;
		internal bool Client;

		public override WindowInfoBase CreateWindow(nint id) => new WindowInfo(id);
		public override WindowInfoBase ActiveWindow()
		{
			ActiveWindowReads++;
			return new WindowInfo(7);
		}
		public override bool IsWindow(nint h) => h != 0 && Exists;
		public override bool GetExists(nint h) => IsWindow(h);
		public override FormWindowState GetWindowState(nint h) => Minimized ? FormWindowState.Minimized : FormWindowState.Normal;
		public override Rectangle GetBounds(nint h) => Bounds(h, false);
		public override Rectangle GetClientBounds(nint h) => Bounds(h, true);
		public override POINT ClientToScreen(nint h)
		{
			var bounds = Bounds(h, true);
			return new POINT(bounds.X, bounds.Y);
		}

		public override bool TryGetBounds(nint h, bool client, out Rectangle bounds)
		{
			if (CloseDuringBounds)
			{
				Exists = false;
				bounds = default;
				return false;
			}

			bounds = GeometryAvailable ? Bounds(h, client) : default;
			return GeometryAvailable;
		}

		private Rectangle Bounds(nint h, bool client)
		{
			Reads++;
			Handle = h;
			Client = client;
			var origin = h == 7 ? new Point(10, 20) : client ? Origin : new Point(290, 170);
			return new Rectangle(origin, new Size(100, 80));
		}
	}

	private sealed class FakeScreen(IScreen inner) : IScreen
	{
		internal readonly List<(int X, int Y)> Pixels = [];
		internal int Captures;

		public IReadOnlyList<DisplayInfo> GetDisplays() => inner.GetDisplays();
		public ScreenRect GetWorkArea(DisplayInfo display) => inner.GetWorkArea(display);
		public bool TryGetPrimarySize(out int width, out int height) => inner.TryGetPrimarySize(out width, out height);
		public ScreenRect GetVirtualScreenBounds() => inner.GetVirtualScreenBounds();
		public DisplayDetails GetDisplayDetails(DisplayInfo display) => inner.GetDisplayDetails(display);
		public bool TryCaptureWindow(nint h, bool includeDecoration, out Bitmap bmp, out PixelScale pixelScale)
		{
			Captures++;
			bmp = null;
			pixelScale = default;
			return false;
		}

		public bool TryGetPixel(int x, int y, out int rgb)
		{
			Pixels.Add((x, y));
			rgb = 0x123456;
			return true;
		}

		public bool TryCaptureRegion(ScreenRect bounds, out Bitmap bmp)
		{
			Captures++;
			bmp = null;
			return false;
		}
	}
}
