#if LINUX
namespace Keysharp.Tests;

[TestFixture, Category("Internal"), Category("Curated")]
public class WindowOperationFailureTests : TestRunner
{
	[TestCase("WinHide", true)]
	[TestCase("WinShow", true)]
	[TestCase("WinMoveTop", true)]
	[TestCase("WinMoveBottom", true)]
	[TestCase("WinHide", false)]
	[TestCase("WinShow", false)]
	[TestCase("WinMoveTop", false)]
	[TestCase("WinMoveBottom", false)]
	public void RoutedFailuresPreserveTheNativeError(string command, bool attempted)
	{
		Exception native = attempted ? new NativeClientException("keysharp-desktop", command,
			NativeClientStatus.Timeout, 77u, 110, "The compositor did not acknowledge the operation.") : null;
		var backend = new FailureBackend(native);
		var windows = new WaylandWindow(backend);
		Assert.That(TryOperation(windows, command, 42, out var failure), Is.False);
		Assert.That(failure, Is.SameAs(native));
		Assert.That(backend.Requests, Is.EqualTo(1));

		using var scope = Keysharp.Runtime.Flow.EnterTry();
		var error = Assert.Throws<KeysharpException>(() => WindowHelper.WindowOperationFailed(command, failure)).UserError;
		Assert.That(error.GetType(), Is.EqualTo(attempted ? typeof(OSError) : typeof(UnsupportedError)));
		Assert.That(error.Message, Does.Contain(command));
		if (attempted) Assert.That(error.Message, Does.Contain(native.Message));
	}

	[Test]
	public void TimeoutSurvivesDesktopRetirement()
	{
		var native = new NativeClientException("keysharp-desktop", "hide window",
			NativeClientStatus.Timeout, 0u, 0, "Original withdrawal timeout.");
		var windows = new WaylandWindow(new FailureBackend(native));
		s.LinuxServices.Desktop.Dispose();
		Assert.That(windows.TryHide(42, out var failure), Is.False);
		Assert.That(failure, Is.SameAs(native));
		using var scope = Keysharp.Runtime.Flow.EnterTry();
		var error = Assert.Throws<KeysharpException>(() => WindowHelper.WindowOperationFailed("WinHide", failure)).UserError;
		Assert.That(error.GetType(), Is.EqualTo(typeof(OSError)));
		Assert.That(error.Message, Does.Contain("Original withdrawal timeout."));
	}

	[Test]
	public void OwnWindowRestackingKeepsItsUnsupportedRoute()
	{
		SkipIfUiInitializationBlocked("Own-window routing needs the local UI toolkit.");
		s.InvokeOnUIThread(() =>
		{
			var gui = new Gui();
			try
			{
				var backend = new FailureBackend(new IOException("The broker must not receive an own-window request."));
				foreach (var windows in new IWindow[] { new X11Window(), new WaylandWindow(backend) })
				{
					Assert.That(windows.TrySetZOrder((nint)gui.Hwnd, ZOrder.Top, out var failure), Is.False);
					Assert.That(failure, Is.Null);
					using var scope = Keysharp.Runtime.Flow.EnterTry();
					var error = Assert.Throws<KeysharpException>(() =>
						WindowHelper.WindowOperationFailed("WinMoveTop", failure)).UserError;
					Assert.That(error.GetType(), Is.EqualTo(typeof(UnsupportedError)));
				}
				Assert.That(backend.Requests, Is.Zero);
			}
			finally { _ = gui.Destroy(); }
		});
	}

	[Test]
	public void FailedOperationContinuesOnce()
	{
		var windows = new WaylandWindow(new FailureBackend(new IOException("Original native failure.")));
		Assert.That(windows.TryHide(42, out var failure), Is.False);
		var reported = 0;
		_ = Errors.OnError(new KeysharpFunc((Func<object, object, object>)((error, _) =>
		{
			Assert.That(error.GetType(), Is.EqualTo(typeof(OSError)));
			reported++;
			return -1L;
		})));
		Assert.That(WindowHelper.WindowOperationFailed("WinHide", failure), Is.SameAs(Script.DefaultObject));
		Assert.That(reported, Is.EqualTo(1));
	}

	private static bool TryOperation(IWindow windows, string command, nint handle, out Exception failure)
	{
		return command switch
		{
			"WinHide" => windows.TryHide(handle, out failure),
			"WinShow" => windows.TryShow(handle, out failure),
			"WinMoveTop" => windows.TrySetZOrder(handle, ZOrder.Top, out failure),
			_ => windows.TrySetZOrder(handle, ZOrder.Bottom, out failure)
		};
	}

	private sealed class FailureBackend(Exception failure) : IWaylandBackend
	{
		public string BackendKey => "window-failure-test";
		public string Name => BackendKey;
		internal int Requests;
		public bool IsKnown(nint handle) => handle == 42;
		public bool TrySetWindowVisible(nint handle, bool visible, out Exception error)
		{
			Requests++;
			error = failure;
			return false;
		}
		public bool TrySetZOrder(nint handle, ZOrder order, out Exception error)
		{
			Requests++;
			error = failure;
			return false;
		}
		public bool TryGetCursorPos(out int x, out int y)
		{
			x = y = 0;
			return false;
		}
	}
}
#endif
