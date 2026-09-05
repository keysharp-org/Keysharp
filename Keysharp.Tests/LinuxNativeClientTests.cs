#if LINUX
using System.Runtime.InteropServices;
using Keysharp.Internals.Input.Linux;
using Keysharp.Internals.Linux;
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

		/// <summary>Gamepad access is ungated, so a connection that asked for no scope can still
		/// enumerate gamepads and read one. A machine with none simply lists none.</summary>
		[Test]
		public void GamepadsAreReadableWithoutAGrant()
		{
			RequireLibrary("libkeysharp-input.so.0", typeof(KeysharpInputClient).Assembly);
			using var client = KeysharpInputClient.Connect();
			Assert.That(client.GrantedScopes, Is.EqualTo(LinuxPermissionScope.None));
			var gamepads = client.ListGamepads(out var generation);
			Assert.That(gamepads, Is.Not.Null);

			foreach (var gamepad in gamepads)
			{
				Assert.That(client.TryGetGamepadState(gamepad.DeviceId, generation, out var state), Is.True);
				Assert.That(state.DeviceId, Is.EqualTo(gamepad.DeviceId));
				Assert.That(state.ButtonCount, Is.EqualTo(gamepad.ButtonCount));
				Assert.That(state.AxisValues, Has.Length.EqualTo(gamepad.Axes.Length));
			}

			Assert.That(client.TryGetGamepadState(uint.MaxValue, generation, out _), Is.False);
		}

		/// <summary>A managed mirror smaller than its native struct would let an init call write past
		/// the end of it, so the sizes are checked against the installed library rather than assumed.</summary>
		[Test]
		public void NativeStructMirrorsMatchTheInstalledLibrary()
		{
			RequireLibrary("libkeysharp-input.so.0", typeof(KeysharpInputClient).Assembly);
			AssertStructSize("NativeDeviceInfo", ksi_device_info_init);
			AssertStructSize("NativeDeviceAxisInfo", ksi_device_axis_info_init);
			AssertStructSize("NativeGamepadState", ksi_gamepad_state_init);
			AssertStructSize("NativeGamepadAxisState", ksi_gamepad_axis_state_init);
		}

		private static void AssertStructSize(string name, Action<byte[]> initialize)
		{
			var buffer = new byte[8192];
			initialize(buffer);
			var mirror = typeof(KeysharpInputClient).GetNestedType(name,
				System.Reflection.BindingFlags.NonPublic);
			Assert.That(mirror, Is.Not.Null, $"{name} is missing.");
			Assert.That(Marshal.SizeOf(mirror), Is.EqualTo((int)BitConverter.ToUInt32(buffer)),
				$"{name} does not match the installed library.");
		}

		[DllImport("libkeysharp-input.so.0", CallingConvention = CallingConvention.Cdecl)]
		private static extern void ksi_device_info_init(byte[] device);
		[DllImport("libkeysharp-input.so.0", CallingConvention = CallingConvention.Cdecl)]
		private static extern void ksi_device_axis_info_init(byte[] axis);
		[DllImport("libkeysharp-input.so.0", CallingConvention = CallingConvention.Cdecl)]
		private static extern void ksi_gamepad_state_init(byte[] state);
		[DllImport("libkeysharp-input.so.0", CallingConvention = CallingConvention.Cdecl)]
		private static extern void ksi_gamepad_axis_state_init(byte[] axis);

		private static void RequireLibrary(string name, System.Reflection.Assembly assembly)
		{
			if (!NativeLibrary.TryLoad(name, assembly, null, out var handle))
				Assert.Ignore($"{name} is not installed.");

			NativeLibrary.Free(handle);
		}
	}
}
#endif
