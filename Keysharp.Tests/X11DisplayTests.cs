#if LINUX
using Keysharp.Internals;
using Keysharp.Internals.Window.Linux.X11;

namespace Keysharp.Tests
{
	[TestFixture, Category("Internal"), Category("Curated")]
	public class X11DisplayTests
	{
		[Test]
		public void MixedScaleSeam()
		{
			ScreenRect[] native =
			[
				new(-3000, 0, 3000, 1800),
				new(0, 0, 1920, 1080)
			];
			ScreenRect[] toolkit =
			[
				new(-2000, 0, 2000, 1200),
				new(0, 0, 1920, 1080)
			];
			var nativeBounds = new ScreenRect(-600, 150, 1200, 450);

			var toolkitBounds = X11DisplayTopology.MapAcrossDisplays(nativeBounds, native, toolkit);

			Assert.That(toolkitBounds, Is.EqualTo(new ScreenRect(-400, 100, 1000, 500)));
			Assert.That(X11DisplayTopology.MapAcrossDisplays(toolkitBounds, toolkit, native),
				Is.EqualTo(nativeBounds));
		}

		[Test]
		public void NegativeOrigin()
		{
			ScreenRect[] native =
			[
				new(-3000, -900, 3000, 1800),
				new(0, 0, 1920, 1080)
			];
			ScreenRect[] toolkit =
			[
				new(-2000, -600, 2000, 1200),
				new(0, 0, 1920, 1080)
			];
			var nativeBounds = new ScreenRect(-2700, -750, 900, 450);

			var toolkitBounds = X11DisplayTopology.MapAcrossDisplays(nativeBounds, native, toolkit);

			Assert.That(toolkitBounds, Is.EqualTo(new ScreenRect(-1800, -500, 600, 300)));
			Assert.That(X11DisplayTopology.MapAcrossDisplays(toolkitBounds, toolkit, native),
				Is.EqualTo(nativeBounds));
		}

		/// <summary>
		/// Startup must survive having no display server, so scripts can run over ssh, in a container, or in CI.
		/// Every Linux startup resolves the platform host, which asks whether X11 is available; that probe used
		/// XOpenDisplay's result without testing it for NULL, so the answer "there is no display" was a
		/// segmentation fault before argument parsing — taking --version down with it.
		/// A separate process because the display is opened once per thread and cached, and this test host has one.
		/// </summary>
		[Test]
		public void StartsWithoutADisplay()
		{
			using var process = new Process
			{
				StartInfo = new ProcessStartInfo("dotnet")
				{
					RedirectStandardOutput = true,
					RedirectStandardError = true,
					UseShellExecute = false,
					CreateNoWindow = true,
				}
			};
			process.StartInfo.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "Keysharp.dll"));
			process.StartInfo.ArgumentList.Add("--version");
			// A daemon started from this session would already hold a display, and answer without the child ever
			// having to resolve one.
			process.StartInfo.Environment["KEYSHARP_DAEMON"] = "0";
			_ = process.StartInfo.Environment.Remove("DISPLAY");
			_ = process.StartInfo.Environment.Remove("WAYLAND_DISPLAY");

			process.Start();
			var output = process.StandardOutput.ReadToEndAsync();
			var error = process.StandardError.ReadToEndAsync();

			if (!process.WaitForExit(120000))
			{
				try { process.Kill(true); } catch { }

				Assert.Fail("Keysharp did not exit within 120 seconds with no display server.");
			}

			// 139 is the shell's spelling of SIGSEGV; .NET reports the raw negative signal.
			Assert.That(process.ExitCode, Is.Zero,
				$"Keysharp exited {process.ExitCode} with no display server.\nstdout: {output.Result}\nstderr: {error.Result}");
			Assert.That(output.Result.Trim(), Is.Not.Empty, "--version printed nothing with no display server.");
		}
	}
}
#endif
