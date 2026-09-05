#if LINUX
using System.Runtime.InteropServices;
using Keysharp.Internals.Input.Linux;
using Keysharp.Internals.Window.Linux.Wayland;

namespace Keysharp.Tests
{
	[TestFixture, Category("Internal"), Category("Curated")]
	public class LinuxNativeClientTests
	{
		[Test]
		public void InputClientConnectsToInstalledService()
		{
			RequireLibrary("libkeysharp-input.so.0", typeof(KeysharpInputClient).Assembly);
			using var client = KeysharpInputClient.Connect();
			Assert.That(client.IsConnected, Is.True);
		}

		[Test]
		public void DesktopClientConnectsToInstalledService()
		{
			RequireLibrary("libkeysharp-desktop.so.0", typeof(DesktopClient).Assembly);
			Assert.That(DesktopClient.ProbeProvider(), Is.True);
		}

		private static void RequireLibrary(string name, System.Reflection.Assembly assembly)
		{
			if (!NativeLibrary.TryLoad(name, assembly, null, out var handle))
				Assert.Ignore($"{name} is not installed.");

			NativeLibrary.Free(handle);
		}
	}
}
#endif
