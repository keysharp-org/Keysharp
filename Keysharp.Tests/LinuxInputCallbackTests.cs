#if LINUX

namespace Keysharp.Tests
{
	[TestFixture, Category("Internal"), Category("Curated")]
	public unsafe class LinuxInputCallbackTests
	{
		[Test]
		public void ModifiedReplyRemainsOwnedAfterManagedCallbackReturns()
		{
			const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
			var type = typeof(KeysharpInputClient);
			using var owner = new LinuxConnectionOwner("callback ownership test", () => -1, () => false, () => { });
			owner.Start();
			var client = (KeysharpInputClient)type.GetConstructors(flags).Single().Invoke(
				[(nint)1, KeysharpInputClient.ConnectionRole.CallbackStream,
				 LinuxPermissionScope.InputControl, KeysharpInputClient.Operations.BlockInput, owner, null, 0UL]);
			var callback = type.GetMethod("HandleNestedHook", flags);
			var parameters = callback.GetParameters();
			var eventType = parameters[0].ParameterType.GetElementType();
			var replyType = parameters[1].ParameterType.GetElementType();
			var hookEvent = NativeMemory.AllocZeroed((nuint)Marshal.SizeOf(eventType));
			var reply = NativeMemory.AllocZeroed((nuint)Marshal.SizeOf(replyType));
			var handlerField = type.GetField("nestedHookEventHandler", flags);
			var buffers = (nint[])type.GetField("nestedReplacementBuffers", flags).GetValue(client);
			try
			{
				Marshal.WriteInt32((nint)hookEvent, 4, (int)KeysharpInputClient.HookType.KeyboardLowLevel);
				Marshal.WriteInt64((nint)hookEvent, 8, 42);
				handlerField.SetValue(client, (Action<KeysharpInputClient, KeysharpInputClient.HookEvent>)
					((sender, message) => sender.SendHookDecision(message.EventId,
						KeysharpInputClient.HookDecision.Modify, [KeysharpInputClient.Input.Key(65)])));
				object[] arguments = [Pointer.Box(hookEvent, parameters[0].ParameterType),
					Pointer.Box(reply, parameters[1].ParameterType)];
				owner.Invoke(() => callback.Invoke(client, arguments));

				// The native serializer reads this pointer only after the delegate has returned.
				var replacement = Marshal.ReadIntPtr((nint)reply, 8);
				Assert.That(replacement, Is.Not.EqualTo(nint.Zero));
				Assert.That(buffers[0], Is.EqualTo(replacement), "The callback released native reply storage too early.");
				Assert.That(Marshal.ReadInt16(replacement, 8), Is.EqualTo(65));

				handlerField.SetValue(client, null);
				owner.Invoke(() => callback.Invoke(client, arguments));
				Assert.That(buffers[0], Is.EqualTo(nint.Zero));
				Assert.That(Marshal.ReadIntPtr((nint)reply, 8), Is.EqualTo(nint.Zero));
			}
			finally
			{
				foreach (var buffer in buffers) NativeMemory.Free((void*)buffer);
				NativeMemory.Free(reply);
				NativeMemory.Free(hookEvent);
			}
		}
	}
}
namespace Keysharp.Tests
{
	[TestFixture, Category("Internal"), Category("Curated")]
	public class LinuxInputLifecycleTests
	{
		[Test]
		public void IncompatibleProtocolIsAConnectionFailure()
		{
			var error = new NativeClientException("keysharp-input", "connect", NativeClientStatus.Unsupported,
				0, 0, "keysharp-input requires protocol 3.0 (client ABI 1.0)");
			var classify = typeof(KeysharpInputManager).GetMethod("IsConnectException", BindingFlags.Static | BindingFlags.NonPublic);
			Assert.That(classify.Invoke(null, [error]), Is.True);
			Assert.That(error.Message, Does.Contain("requires protocol 3.0"));
		}

		[Test]
		public void PromptAndPendingRpcDoNotBlockShutdown()
		{
			const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
			using var service = new KeysharpInputManager();
			const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
			var manager = typeof(KeysharpInputManager);
			var clients = manager.GetField("client", fields);
			Assert.That(clients.GetValue(service), Is.Null);
			var query = manager.GetField("queries", fields).GetValue(service);
			var cached = query.GetType().GetField("current", instance);
			Assert.That(cached.GetValue(query), Is.Null);
			using var leaseOwner = new LinuxConnectionOwner("fake input lease", () => -1, () => false, () => { });
			using var queryOwner = new LinuxConnectionOwner("fake input rpc", () => -1, () => false, () => { });
			leaseOwner.Start();
			queryOwner.Start();
			var constructor = typeof(KeysharpInputClient).GetConstructors(instance).Single();
			var lease = (KeysharpInputClient)constructor.Invoke([(nint)1, KeysharpInputClient.ConnectionRole.Lease,
				LinuxPermissionScope.InputControl, KeysharpInputClient.Operations.BlockInput, leaseOwner, null, 42UL]);
			var rpc = (KeysharpInputClient)constructor.Invoke([(nint)1, KeysharpInputClient.ConnectionRole.Rpc,
				LinuxPermissionScope.InputControl, KeysharpInputClient.Operations.BlockInput, queryOwner, lease, 42UL]);
			using var promptEntered = new ManualResetEventSlim();
			using var rpcEntered = new ManualResetEventSlim();
			using var release = new ManualResetEventSlim();
			clients.SetValue(service, lease);
			cached.SetValue(query, rpc);
			var prompt = Task.Run(() =>
			{
				lock ((Lock)manager.GetField("authorizationGate", fields).GetValue(service))
				{
					promptEntered.Set();
					Assert.That(release.Wait(2_000), Is.True);
				}
			});
			var use = query.GetType().GetMethod("TryUse", instance, [typeof(Func<KeysharpInputClient, bool>)]);
			Task<bool> request = null;
			Task shutdown = null;
			try
			{
				Assert.That(promptEntered.Wait(1_000), Is.True);
				request = Task.Run(() => (bool)use.Invoke(query, [(Func<KeysharpInputClient, bool>)(_ =>
				{
					rpcEntered.Set();
					return release.Wait(2_000);
				})]));
				Assert.That(rpcEntered.Wait(500), Is.True, "An existing RPC lane must bypass the authorization lock.");
				var elapsed = Stopwatch.StartNew();
				shutdown = Task.Run(() => service.Dispose());
				Assert.That(shutdown.Wait(500), Is.True, "Shutdown must retire clients while authorization and an RPC are pending.");
				Assert.That(elapsed.ElapsedMilliseconds, Is.LessThan(500));
				Assert.That(service.AuthorizationLease, Is.Null);
				Assert.That(service.HasInputOperation(KeysharpInputClient.Operations.BlockInput), Is.False);
				Assert.That(request.IsCompleted, Is.False);
			}
			finally
			{
				release.Set();
				Assert.That(prompt.Wait(1_000), Is.True);
				if (shutdown != null) Assert.That(shutdown.Wait(1_000), Is.True);
				if (request != null) Assert.That(request.Wait(1_000), Is.True);
				service.Dispose();
			}
			Assert.That(request.Result, Is.False, "A retired RPC must not report a successful result in a later session.");
		}
	}
}

namespace Keysharp.Tests
{
	[TestFixture, Category("Internal"), Category("Curated")]
	public class LinuxInputStateTests
	{
		[Test]
		public void SnapshotPrecedesDeltasAndGapRequestsOneResync()
		{
			var mirror = new KeysharpInputClient.KeyboardMirror();
			Assert.That(mirror.TryRead(out _, out _), Is.False);
			mirror.Publish(8, State(1), 2, initial: true);
			Assert.That(mirror.Publish(9, State(3), 4, initial: false), Is.True);
			Assert.That(mirror.TryRead(out var state, out var physical), Is.True);
			Assert.That(state.ModifiersLR, Is.EqualTo(3));
			Assert.That(physical, Is.EqualTo(4));
			Assert.That(mirror.Publish(11, State(5), 6, initial: false), Is.False);
			Assert.That(mirror.TryRead(out _, out _), Is.False);
			Assert.That(mirror.Publish(12, State(7), 8, initial: false), Is.True,
				"While awaiting a fresh snapshot, more deltas must not issue more resync requests.");
			Assert.That(mirror.TryRead(out _, out _), Is.False);
			mirror.Publish(12, State(7), 8, initial: true);
			Assert.That(mirror.TryRead(out state, out physical), Is.True);
			Assert.That(state.ModifiersLR, Is.EqualTo(7));
			Assert.That(physical, Is.EqualTo(8));
		}

		[Test]
		public void SynthesisAcknowledgementPreservesNewerPhysicalState()
		{
			var mirror = new KeysharpInputClient.KeyboardMirror();
			mirror.Publish(20, State(0), 0, initial: true);
			mirror.Acknowledge(21, 1);
			Assert.That(mirror.TryRead(out var state, out _), Is.True);
			Assert.That(state.ModifiersLR, Is.EqualTo(1));
			Assert.That(mirror.Publish(21, State(1), 0, initial: false), Is.True,
				"An acknowledgement must not advance the pushed delta cursor.");
			mirror.Publish(22, State(3), 2, initial: false);
			mirror.Acknowledge(21, 1);
			Assert.That(mirror.TryRead(out state, out var physical), Is.True);
			Assert.That(state.ModifiersLR, Is.EqualTo(3));
			Assert.That(physical, Is.EqualTo(2));
			mirror.Invalidate();
			Assert.That(mirror.TryRead(out _, out _), Is.False);
		}

		private static KeysharpInputClient.KeyStateSnapshot State(uint logical)
			=> new(logical, false, false, false, default, default);

		[Test]
		public void ToggleObservationWaitsForSynthesisPublication()
		{
			var mirror = new KeysharpInputClient.KeyboardMirror();
			mirror.Publish(1, State(0), 0, initial: true);
			mirror.Acknowledge(2, 1);
			using var entered = new ManualResetEventSlim();
			var wait = Task.Run(() => { entered.Set(); return mirror.WaitForAcknowledgement(1_000); });
			Assert.That(entered.Wait(1_000), Is.True);
			Assert.That(wait.IsCompleted, Is.False);
			mirror.Publish(2, State(1) with { CapsLock = true }, 0, initial: false);
			Assert.That(wait.Wait(1_000), Is.True);
			Assert.That(wait.Result, Is.True);
			Assert.That(mirror.TryRead(out var state, out _), Is.True);
			Assert.That(state.CapsLock, Is.True);
			mirror.Acknowledge(3, 0);
			Assert.That(mirror.WaitForAcknowledgement(20), Is.False);
		}
	}
}
#endif
