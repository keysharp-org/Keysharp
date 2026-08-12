using Assert = NUnit.Framework.Legacy.ClassicAssert;

#if LINUX
using System.Buffers.Binary;
using System.Net.Sockets;
using Keysharp.Internals.Input.Linux;
#endif

namespace Keysharp.Tests
{
	[Category("Internal"), Category("Curated")]
	public class LinuxInputdProtocolTests
	{
#if LINUX
		[Test, Category("Misc")]
		public void ProtocolValues()
		{
			Assert.AreEqual(2u, (uint)KeysharpInputdClient.HookDecision.Modify);
			Assert.AreEqual(12u, (uint)KeysharpInputdClient.StatusDetail.ResourceExhausted);
			Assert.AreEqual(32u, (uint)KeysharpInputdClient.StatusDetail.RecursionLimit);
			Assert.AreEqual(33u, (uint)KeysharpInputdClient.StatusDetail.ExpandedInputLimit);
			Assert.AreEqual(403u, (uint)KeysharpInputdClient.StatusDetail.PermissionDenied);
			Assert.AreEqual(408u, (uint)KeysharpInputdClient.StatusDetail.CallbackTimeout);
			Assert.AreEqual(2u, (uint)KeysharpInputdClient.HookDecisionDetail.StaleOrWrongResponder);
			Assert.AreEqual(4u, (uint)KeysharpInputdClient.HookDecisionDetail.InvalidDecision);
			Assert.AreEqual(7u, (uint)KeysharpInputdClient.HookDecisionDetail.EmptyModify);
			Assert.AreEqual(48u, (uint)KeysharpInputdClient.MessageType.IdleTime);
		}

		[Test, Category("Misc")]
		public async Task IdleTimeQuery()
		{
			var path = $"/tmp/keysharp-inputd-test-{Environment.ProcessId}-{Guid.NewGuid():N}.sock";
			var previous = Environment.GetEnvironmentVariable(KeysharpInputdClient.SocketEnvironmentVariable);
			using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
			listener.Bind(new UnixDomainSocketEndPoint(path));
			listener.Listen(1);
			Environment.SetEnvironmentVariable(KeysharpInputdClient.SocketEnvironmentVariable, path);

			try
			{
				var server = Task.Run(async () =>
				{
					using var socket = await listener.AcceptAsync();
					var hello = ReceiveFrame(socket);
					Assert.AreEqual(KeysharpInputdClient.MessageType.ClientHello, hello.Type);
					SendFrame(socket, KeysharpInputdClient.MessageType.ClientHello,
						hello.CorrelationId, new byte[24]);

					var currentQuery = ReceiveFrame(socket);
					Assert.AreEqual(KeysharpInputdClient.MessageType.IdleTime, currentQuery.Type);
					var currentPayload = new byte[16];
					currentPayload[0] = 1;
					BinaryPrimitives.WriteUInt64LittleEndian(currentPayload.AsSpan(8), 123456);
					SendFrame(socket, KeysharpInputdClient.MessageType.IdleTime,
						currentQuery.CorrelationId, currentPayload);

					var oldDaemonQuery = ReceiveFrame(socket);
					Assert.AreEqual(KeysharpInputdClient.MessageType.IdleTime, oldDaemonQuery.Type);
					SendStatus(socket, KeysharpInputdClient.MessageType.IdleTime,
						oldDaemonQuery.CorrelationId, -1, 404);
				});

				using var client = KeysharpInputdClient.Connect();
				Assert.IsTrue(client.TryGetIdleTime(out var milliseconds));
				Assert.AreEqual(123456ul, milliseconds);
				Assert.IsFalse(client.TryGetIdleTime(out _));
				await server.WaitAsync(TimeSpan.FromSeconds(5));
			}
			finally
			{
				Environment.SetEnvironmentVariable(KeysharpInputdClient.SocketEnvironmentVariable, previous);
				try { File.Delete(path); } catch { }
			}
		}

		[Test, Category("Misc")]
		public async Task PointerButtons()
		{
			var path = $"/tmp/keysharp-inputd-test-{Environment.ProcessId}-{Guid.NewGuid():N}.sock";
			var previous = Environment.GetEnvironmentVariable(KeysharpInputdClient.SocketEnvironmentVariable);
			using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
			listener.Bind(new UnixDomainSocketEndPoint(path));
			listener.Listen(1);
			Environment.SetEnvironmentVariable(KeysharpInputdClient.SocketEnvironmentVariable, path);

			try
			{
				var server = Task.Run(async () =>
				{
					using var socket = await listener.AcceptAsync();
					var hello = ReceiveFrame(socket);
					SendFrame(socket, KeysharpInputdClient.MessageType.ClientHello,
						hello.CorrelationId, new byte[24]);

					var query = ReceiveFrame(socket);
					Assert.AreEqual(KeysharpInputdClient.MessageType.GetPointerButtons, query.Type);
					var payload = new byte[16];
					payload[0] = 1;
					BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8), (1u << 0) | (1u << 3));
					BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(12), 1u << 4);
					SendFrame(socket, KeysharpInputdClient.MessageType.PointerButtonsResult,
						query.CorrelationId, payload);
				});

				using var client = KeysharpInputdClient.Connect();
				Assert.IsTrue(client.TryGetPointerButtons(out var buttons));
				Assert.AreEqual((1u << 0) | (1u << 3), buttons.LogicalButtons);
				Assert.AreEqual(1u << 4, buttons.PhysicalButtons);
				await server.WaitAsync(TimeSpan.FromSeconds(5));
			}
			finally
			{
				Environment.SetEnvironmentVariable(KeysharpInputdClient.SocketEnvironmentVariable, previous);
				try { File.Delete(path); } catch { }
			}
		}

		[Test, Category("Misc")]
		public async Task NestedHookEvents()
		{
			var path = $"/tmp/keysharp-inputd-test-{Environment.ProcessId}-{Guid.NewGuid():N}.sock";
			var previous = Environment.GetEnvironmentVariable(KeysharpInputdClient.SocketEnvironmentVariable);
			using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
			listener.Bind(new UnixDomainSocketEndPoint(path));
			listener.Listen(1);
			Environment.SetEnvironmentVariable(KeysharpInputdClient.SocketEnvironmentVariable, path);
			try
			{
				var server = Task.Run(async () =>
				{
					using var socket = await listener.AcceptAsync();
					var hello = ReceiveFrame(socket);
					Assert.AreEqual(KeysharpInputdClient.MessageType.ClientHello, hello.Type);
					Assert.AreEqual(32, hello.Payload.Length);
					Assert.AreEqual((uint)KeysharpInputdClient.ConnectionRole.HookStream,
						BinaryPrimitives.ReadUInt32LittleEndian(hello.Payload.AsSpan(8)));
					var helloResult = new byte[24];
					SendFrame(socket, KeysharpInputdClient.MessageType.ClientHello,
						hello.CorrelationId, helloResult);

					var synthesis = ReceiveFrame(socket);
					Assert.AreEqual(KeysharpInputdClient.MessageType.SynthesizeInput, synthesis.Type);
					Assert.AreEqual(77ul, synthesis.CorrelationId);
					Assert.AreEqual(48, synthesis.Payload.Length);
					var hookPayload = new byte[56];
					BinaryPrimitives.WriteUInt64LittleEndian(hookPayload, 77);
					BinaryPrimitives.WriteUInt32LittleEndian(hookPayload.AsSpan(8),
						(uint)KeysharpInputdClient.HookType.KeyboardLowLevel);
					BinaryPrimitives.WriteUInt32LittleEndian(hookPayload.AsSpan(16), 0x0100);
					BinaryPrimitives.WriteUInt32LittleEndian(hookPayload.AsSpan(20), 0x41);
					BinaryPrimitives.WriteUInt32LittleEndian(hookPayload.AsSpan(28), 0x10);
					BinaryPrimitives.WriteUInt32LittleEndian(hookPayload.AsSpan(48), 1234);
					SendFrame(socket, KeysharpInputdClient.MessageType.HookEvent, 77, hookPayload);

					var stateQuery = ReceiveFrame(socket);
					Assert.AreEqual(KeysharpInputdClient.MessageType.GetKeyState, stateQuery.Type);
					SendFrame(socket, KeysharpInputdClient.MessageType.KeyStateResult,
						stateQuery.CorrelationId, new byte[8]);

					var decision = ReceiveFrame(socket);
					Assert.AreEqual(KeysharpInputdClient.MessageType.HookDecision, decision.Type);
					Assert.AreEqual(77ul, decision.CorrelationId);
					SendStatus(socket, KeysharpInputdClient.MessageType.HookDecision,
						decision.CorrelationId, 0, 0);
					SendStatus(socket, KeysharpInputdClient.MessageType.SynthesisResult,
						synthesis.CorrelationId, 0, 0);
				});

				using var client = KeysharpInputdClient.Connect(
					role: KeysharpInputdClient.ConnectionRole.HookStream);
				var nestedCount = 0;
				client.SetNestedHookEventHandler((rpc, hookEvent) =>
				{
					Assert.AreEqual(77ul, hookEvent.EventId);
					Assert.AreEqual(0x10u, hookEvent.Keyboard.Flags);
					Assert.AreEqual(1234u, hookEvent.Keyboard.DeviceId);
					// Recursive requests stay on the HookStream's synchronous callback stack.
					rpc.QueryKeyState();
					nestedCount++;
					rpc.SendHookDecision(hookEvent.EventId, KeysharpInputdClient.HookDecision.Pass);
				});

				client.SendInput([KeysharpInputdClient.Input.Key(0x41)], parentHookEventId: 77);
				await server.WaitAsync(TimeSpan.FromSeconds(5));
				Assert.AreEqual(1, nestedCount);
			}
			finally
			{
				Environment.SetEnvironmentVariable(KeysharpInputdClient.SocketEnvironmentVariable, previous);
				try { File.Delete(path); } catch { }
			}
		}

		[Test, Category("Misc")]
		public async Task SubscriptionRace()
		{
			var path = $"/tmp/keysharp-inputd-test-{Environment.ProcessId}-{Guid.NewGuid():N}.sock";
			var previous = Environment.GetEnvironmentVariable(KeysharpInputdClient.SocketEnvironmentVariable);
			using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
			listener.Bind(new UnixDomainSocketEndPoint(path));
			listener.Listen(1);
			Environment.SetEnvironmentVariable(KeysharpInputdClient.SocketEnvironmentVariable, path);
			try
			{
				var server = Task.Run(async () =>
				{
					using var socket = await listener.AcceptAsync();
					var hello = ReceiveFrame(socket);
					SendFrame(socket, KeysharpInputdClient.MessageType.ClientHello,
						hello.CorrelationId, new byte[24]);

					var subscribe = ReceiveFrame(socket);
					Assert.AreEqual(KeysharpInputdClient.MessageType.SubscribeHook, subscribe.Type);

					// An already-subscribed lane emitted this before the ack was written.
					var hookPayload = new byte[56];
					BinaryPrimitives.WriteUInt64LittleEndian(hookPayload, 775);
					BinaryPrimitives.WriteUInt32LittleEndian(hookPayload.AsSpan(8),
						(uint)KeysharpInputdClient.HookType.KeyboardLowLevel);
					BinaryPrimitives.WriteUInt32LittleEndian(hookPayload.AsSpan(16), 0x0100);
					BinaryPrimitives.WriteUInt32LittleEndian(hookPayload.AsSpan(20), 0x41);
					SendFrame(socket, KeysharpInputdClient.MessageType.HookEvent, 775, hookPayload);
					SendStatus(socket, KeysharpInputdClient.MessageType.SubscribeHook,
						subscribe.CorrelationId, 0, 1);

					var decision = ReceiveFrame(socket);
					Assert.AreEqual(KeysharpInputdClient.MessageType.HookDecision, decision.Type);
					Assert.AreEqual(775ul, decision.CorrelationId);
					SendStatus(socket, KeysharpInputdClient.MessageType.HookDecision,
						decision.CorrelationId, 0, 0);
				});

				using var client = KeysharpInputdClient.Connect(
					role: KeysharpInputdClient.ConnectionRole.HookStream);
				var nestedCount = 0;
				client.SetNestedHookEventHandler((rpc, hookEvent) =>
				{
					Assert.AreEqual(775ul, hookEvent.EventId);
					nestedCount++;
					rpc.SendHookDecision(hookEvent.EventId, KeysharpInputdClient.HookDecision.Pass);
				});

				Assert.AreEqual(1u, client.SubscribeHook(KeysharpInputdClient.HookType.KeyboardLowLevel));
				await server.WaitAsync(TimeSpan.FromSeconds(5));
				Assert.AreEqual(1, nestedCount);
			}
			finally
			{
				Environment.SetEnvironmentVariable(KeysharpInputdClient.SocketEnvironmentVariable, previous);
				try { File.Delete(path); } catch { }
			}
		}

		[Test, Category("Misc")]
		public async Task OrphanedResponses()
		{
			var path = $"/tmp/keysharp-inputd-test-{Environment.ProcessId}-{Guid.NewGuid():N}.sock";
			var previous = Environment.GetEnvironmentVariable(KeysharpInputdClient.SocketEnvironmentVariable);
			using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
			listener.Bind(new UnixDomainSocketEndPoint(path));
			listener.Listen(1);
			Environment.SetEnvironmentVariable(KeysharpInputdClient.SocketEnvironmentVariable, path);
			try
			{
				var server = Task.Run(async () =>
				{
					using var socket = await listener.AcceptAsync();
					var hello = ReceiveFrame(socket);
					SendFrame(socket, KeysharpInputdClient.MessageType.ClientHello,
						hello.CorrelationId, new byte[24]);

					// The reply to a request that already timed out, then a real event.
					SendStatus(socket, KeysharpInputdClient.MessageType.SynthesisResult, 99, 0, 0);
					var hookPayload = new byte[72];
					BinaryPrimitives.WriteUInt64LittleEndian(hookPayload, 12);
					BinaryPrimitives.WriteUInt32LittleEndian(hookPayload.AsSpan(8),
						(uint)KeysharpInputdClient.HookType.MouseLowLevel);
					BinaryPrimitives.WriteUInt32LittleEndian(hookPayload.AsSpan(16), 0x0200);
					BinaryPrimitives.WriteInt32LittleEndian(hookPayload.AsSpan(20), 123);
					BinaryPrimitives.WriteInt32LittleEndian(hookPayload.AsSpan(24), 456);
					BinaryPrimitives.WriteUInt32LittleEndian(hookPayload.AsSpan(28), 0x8000);
					BinaryPrimitives.WriteUInt32LittleEndian(hookPayload.AsSpan(56), 42);
					BinaryPrimitives.WriteInt32LittleEndian(hookPayload.AsSpan(64), -3);
					BinaryPrimitives.WriteInt32LittleEndian(hookPayload.AsSpan(68), 4);
					SendFrame(socket, KeysharpInputdClient.MessageType.HookEvent, 12, hookPayload);
					await Task.Delay(50);
				});

				using var client = KeysharpInputdClient.Connect(
					role: KeysharpInputdClient.ConnectionRole.HookStream);
				var hookEvent = client.ReadHookEvent();
				Assert.AreEqual(12ul, hookEvent.EventId);
				Assert.AreEqual(KeysharpInputdClient.HookType.MouseLowLevel, hookEvent.HookType);
				Assert.AreEqual(123, hookEvent.Mouse.X);
				Assert.AreEqual(456, hookEvent.Mouse.Y);
				Assert.AreEqual(42u, hookEvent.Mouse.DeviceId);
				Assert.AreEqual(-3, hookEvent.Mouse.DeltaX);
				Assert.AreEqual(4, hookEvent.Mouse.DeltaY);
				await server.WaitAsync(TimeSpan.FromSeconds(5));
			}
			finally
			{
				Environment.SetEnvironmentVariable(KeysharpInputdClient.SocketEnvironmentVariable, previous);
				try { File.Delete(path); } catch { }
			}
		}

		private readonly record struct TestFrame(
			KeysharpInputdClient.MessageType Type,
			ulong CorrelationId,
			byte[] Payload);

		private static TestFrame ReceiveFrame(Socket socket)
		{
			var header = ReceiveExact(socket, 24);
			var size = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(header));
			Assert.AreEqual(1, BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(4)));
			Assert.AreEqual(1, BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(6)));
			return new(
				(KeysharpInputdClient.MessageType)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8)),
				BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(16)),
				ReceiveExact(socket, size - 24));
		}

		private static void SendStatus(
			Socket socket,
			KeysharpInputdClient.MessageType type,
			ulong correlationId,
			int status,
			uint detail)
		{
			Span<byte> payload = stackalloc byte[8];
			BinaryPrimitives.WriteInt32LittleEndian(payload, status);
			BinaryPrimitives.WriteUInt32LittleEndian(payload[4..], detail);
			SendFrame(socket, type, correlationId, payload);
		}

		private static void SendFrame(
			Socket socket,
			KeysharpInputdClient.MessageType type,
			ulong correlationId,
			ReadOnlySpan<byte> payload)
		{
			Span<byte> header = stackalloc byte[24];
			BinaryPrimitives.WriteUInt32LittleEndian(header, checked((uint)(24 + payload.Length)));
			BinaryPrimitives.WriteUInt16LittleEndian(header[4..], 1);
			BinaryPrimitives.WriteUInt16LittleEndian(header[6..], 1);
			BinaryPrimitives.WriteUInt32LittleEndian(header[8..], (uint)type);
			BinaryPrimitives.WriteUInt64LittleEndian(header[16..], correlationId);
			SendAll(socket, header);
			SendAll(socket, payload);
		}

		private static void SendAll(Socket socket, ReadOnlySpan<byte> bytes)
		{
			var offset = 0;

			while (offset < bytes.Length)
			{
				var sent = socket.Send(bytes[offset..], SocketFlags.None);

				if (sent == 0)
					throw new EndOfStreamException();

				offset += sent;
			}
		}

		private static byte[] ReceiveExact(Socket socket, int size)
		{
			var result = new byte[size];
			var offset = 0;

			while (offset < result.Length)
			{
				var read = socket.Receive(result.AsSpan(offset), SocketFlags.None);

				if (read == 0)
					throw new EndOfStreamException();

				offset += read;
			}

			return result;
		}
#endif
	}
}
