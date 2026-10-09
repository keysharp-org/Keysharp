#if LINUX

namespace Keysharp.Tests
{
	[TestFixture, Category("Internal"), Category("Curated")]
	public class LinuxNativeClientTests
	{
		[Test]
		public void InputClientConnectsToInstalledService()
		{
			RequireLibrary("libkeysharp-input.so.1", typeof(KeysharpInputClient).Assembly);
			using var client = KeysharpInputClient.Connect();
			Assert.That(client.IsConnected, Is.True);
		}

		[Test]
		public void DesktopClientConnectsToInstalledService()
		{
			RequireLibrary("libkeysharp-desktop.so.1", typeof(DesktopClient).Assembly);
			using var services = new LinuxServices();
			Assert.That(services.Desktop.ProbeProvider(), Is.True);
		}

		[Test]
		public void PermissionCheckDoesNotSubscribeToWindowState()
		{
			RequireLibrary("libkeysharp-desktop.so.1", typeof(DesktopClient).Assembly);
			using var services = new LinuxServices();
			var result = services.Permissions.RequestWindowMonitoring(prompt: false);
			Assert.That(result.Status, Is.EqualTo(Keysharp.Internals.Os.PermissionStatus.Granted)
				.Or.EqualTo(Keysharp.Internals.Os.PermissionStatus.Denied), result.Message);
			const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
			var lease = typeof(DesktopClient).GetField("authorizationLease", flags).GetValue(services.Desktop);
			Assert.That(lease, Is.Not.Null);
			var state = lease.GetType().GetProperty("State", flags).GetValue(lease);
			Assert.That(state.GetType().GetField("domains", flags).GetValue(state), Is.EqualTo(16u),
				"A permission check should subscribe only to live grants, without window, keyboard or display snapshots.");
			if (result.IsGranted && services.Desktop.ProviderSupportsWindowWatch())
			{
				_ = services.Desktop.WindowChangeSignal;
				Assert.That(state.GetType().GetField("domains", flags).GetValue(state), Is.EqualTo(17u),
					"Waiting for window changes must start the window subscription on demand.");
			}
		}

		/// <summary>Gamepad access is ungated, so a connection that asked for no scope can still
		/// enumerate gamepads and read one. A machine with none simply lists none.</summary>
		[Test]
		public void GamepadsAreReadableWithoutAGrant()
		{
			RequireLibrary("libkeysharp-input.so.1", typeof(KeysharpInputClient).Assembly);
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
			RequireLibrary("libkeysharp-input.so.1", typeof(KeysharpInputClient).Assembly);
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

		[DllImport("libkeysharp-input.so.1", CallingConvention = CallingConvention.Cdecl)]
		private static extern void ksi_device_info_init(byte[] device);
		[DllImport("libkeysharp-input.so.1", CallingConvention = CallingConvention.Cdecl)]
		private static extern void ksi_device_axis_info_init(byte[] axis);
		[DllImport("libkeysharp-input.so.1", CallingConvention = CallingConvention.Cdecl)]
		private static extern void ksi_gamepad_state_init(byte[] state);
		[DllImport("libkeysharp-input.so.1", CallingConvention = CallingConvention.Cdecl)]
		private static extern void ksi_gamepad_axis_state_init(byte[] axis);

		private static void RequireLibrary(string name, System.Reflection.Assembly assembly)
		{
			if (!NativeLibrary.TryLoad(name, assembly, null, out var handle))
				Assert.Ignore($"{name} is not installed.");

			NativeLibrary.Free(handle);
		}
	}
}
namespace Keysharp.Tests
{
	[TestFixture, Category("Internal"), Category("Curated")]
	public unsafe class LinuxConnectionOwnerTests
	{
		[Test]
		public void BufferedMessagesDrainBeforeWaiting()
		{
			using var stream = new FakeStream();
			using var received = new CountdownEvent(2);
			stream.Queue(() => received.Signal());
			stream.Queue(() => received.Signal());
			using var owner = stream.CreateOwner("buffered readiness");
			owner.Start();
			Assert.That(received.Wait(1_000), Is.True, "The first read clears fd readiness, but the buffered second record must still drain.");
		}

		[TestCase(false), TestCase(true)]
		public void IdleShutdownWakesOwnerAndCleansUpOnIt(bool start)
		{
			using var stream = new FakeStream();
			using var owner = stream.CreateOwner("idle shutdown");
			if (start) owner.Start();
			var thread = start ? owner.Invoke(() => Environment.CurrentManagedThreadId, 1_000) : 0;
			var elapsed = Stopwatch.StartNew();
			owner.Dispose();
			Assert.That(elapsed.ElapsedMilliseconds, Is.LessThan(500));
			Assert.That(stream.CleanupThread, Is.Not.Zero);
			if (start) Assert.That(stream.CleanupThread, Is.EqualTo(thread));
			Assert.Throws<ObjectDisposedException>(owner.Start);
			Assert.That(owner.IsRunning, Is.False);
			Assert.Throws<ObjectDisposedException>(() => owner.Invoke(() => 1, 100));
		}

		[Test]
		public void ShutdownDoesNotWaitForABlockedRpc()
		{
			using var stream = new FakeStream();
			using var owner = stream.CreateOwner("blocked shutdown");
			using var entered = new ManualResetEventSlim();
			using var release = new ManualResetEventSlim();
			owner.Start();
			var request = Task.Run(() => owner.Invoke(() =>
			{
				entered.Set();
				var completed = release.Wait(2_000);
				Assert.Throws<ObjectDisposedException>(() => owner.Invoke(() => 1, 100));
				Assert.Throws<ObjectDisposedException>(() => owner.Post(() => { }));
				return completed;
			}, 3_000));
			var posted = 0;
			try
			{
				Assert.That(entered.Wait(1_000), Is.True);
				var elapsed = Stopwatch.StartNew();
				Assert.Throws<TimeoutException>(() => owner.Invoke(() => Interlocked.Increment(ref posted), 20));
				owner.Post(() => Interlocked.Increment(ref posted));
				owner.Dispose();
				Assert.That(elapsed.ElapsedMilliseconds, Is.LessThan(500));
				Assert.That(owner.IsRunning, Is.False);
				Assert.Throws<ObjectDisposedException>(() => owner.Invoke(() => 1, 100));
			}
			finally { release.Set(); }
			Assert.That(request.Wait(1_000), Is.True);
			Assert.That(posted, Is.Zero, "Queued native work must be cancelled when its owner stops.");
		}

		[Test]
		public void SubscriptionChangesKeepOneOwner()
		{
			using var stream = new FakeStream();
			using var owner = stream.CreateOwner("subscription changes");
			owner.Start();
			var kinds = 1;
			var ownerThread = owner.Invoke(() => Environment.CurrentManagedThreadId, 1_000);
			using var observed = new ManualResetEventSlim();
			owner.Invoke(() => { kinds |= 2; return true; }, 1_000);
			stream.Queue(() =>
			{
				Assert.That(kinds, Is.EqualTo(3));
				Assert.That(Environment.CurrentManagedThreadId, Is.EqualTo(ownerThread));
				observed.Set();
			});
			Assert.That(observed.Wait(1_000), Is.True);
			owner.Invoke(() => { kinds &= ~1; return true; }, 1_000);
			Assert.That(kinds, Is.EqualTo(2));
		}

		[Test]
		public void SlowRpcDoesNotDelayFastLane()
		{
			using var fastStream = new FakeStream();
			using var slowStream = new FakeStream();
			using var fast = fastStream.CreateOwner("fast lane");
			using var slow = slowStream.CreateOwner("slow lane");
			using var entered = new ManualResetEventSlim();
			using var release = new ManualResetEventSlim();
			fast.Start();
			slow.Start();
			var request = Task.Run(() => slow.Invoke(() => { entered.Set(); return release.Wait(2_000); }, 3_000));
			try
			{
				Assert.That(entered.Wait(1_000), Is.True);
				Assert.That(fast.Invoke(() => 42, 500), Is.EqualTo(42));
				Assert.That(request.IsCompleted, Is.False);
			}
			finally { release.Set(); }
			Assert.That(request.Wait(1_000), Is.True);
		}

		private sealed class FakeStream : IDisposable
		{
			private readonly ConcurrentQueue<Action> buffered = new();
			private int fd = eventfd(0, 0x800 | 0x80000);
			internal int CleanupThread;

			internal LinuxConnectionOwner CreateOwner(string name)
				=> new(name, () => fd, Drain, () => CleanupThread = Environment.CurrentManagedThreadId);

			internal void Queue(Action message)
			{
				buffered.Enqueue(message);
				ulong value = 1;
				Assert.That(write(fd, &value, 8), Is.EqualTo((nint)8));
			}

			private bool Drain()
			{
				ulong value;
				read(fd, &value, 8);
				if (!buffered.TryDequeue(out var message)) return false;
				message();
				return true;
			}

			public void Dispose()
			{
				var closed = Interlocked.Exchange(ref fd, -1);
				if (closed >= 0) close(closed);
			}
		}

		[DllImport("libc")] private static extern int eventfd(uint initial, int flags);
		[DllImport("libc")] private static extern nint read(int fd, void* buffer, nuint count);
		[DllImport("libc")] private static extern nint write(int fd, void* buffer, nuint count);
		[DllImport("libc")] private static extern int close(int fd);
	}
}

namespace Keysharp.Tests
{
	[TestFixture, Category("Internal"), Category("Curated")]
	public class LinuxServiceAbiTests
	{
		[TestCase("input", "NativeConnectOptions", "ksi_connect_options_init")]
		[TestCase("input", "NativeServiceInfo", "ksi_service_info_init")]
		[TestCase("input", "NativeLeaseMessage", "ksi_lease_message_init")]
		[TestCase("input", "NativeKeyState", "ksi_key_state_init")]
		[TestCase("input", "NativeHookMessage", "ksi_hook_message_init")]
		[TestCase("desktop", "NativeConnectOptions", "ksd_connect_options_init")]
		[TestCase("desktop", "NativeServiceInfo", "ksd_service_info_init")]
		[TestCase("desktop", "NativeWindowRecord", "ksd_window_record_init")]
		[TestCase("desktop", "NativeStateEvent", "ksd_state_event_init")]
		[TestCase("desktop", "NativeString", "ksd_string_init")]
		public void ManagedRecordsMatchNativeInitializers(string service, string record, string initializer)
		{
			var owner = service == "input" ? typeof(KeysharpInputClient) : typeof(DesktopClient);
			var library = $"libkeysharp-{service}.so.1";
			if (!NativeLibrary.TryLoad(library, owner.Assembly, null, out var handle))
				Assert.Ignore($"{library} is unavailable; the ABI test does not connect to a service.");

			var buffer = Marshal.AllocHGlobal(4_096);
			try
			{
				var prefix = service == "input" ? "ksi" : "ksd";
				var major = Marshal.GetDelegateForFunctionPointer<AbiMajor>(NativeLibrary.GetExport(handle, $"{prefix}_client_abi_major"));
				Assert.That(major(), Is.EqualTo(1u), $"{library} must provide client ABI 1.0 / protocol 3.");
				var initialize = Marshal.GetDelegateForFunctionPointer<Initialize>(NativeLibrary.GetExport(handle, initializer));
				initialize(buffer);
				var nativeSize = Marshal.ReadInt32(buffer);
				var mirror = owner.GetNestedType(record, BindingFlags.NonPublic);
				Assert.That(mirror, Is.Not.Null, $"{record} is missing.");
				Assert.That(Marshal.SizeOf(mirror), Is.EqualTo(nativeSize),
					$"{record} does not match {library} ({RuntimeInformation.ProcessArchitecture}).");
			}
			finally
			{
				Marshal.FreeHGlobal(buffer);
				NativeLibrary.Free(handle);
			}
		}

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Initialize(nint record);
		[UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint AbiMajor();
	}
}
#endif
