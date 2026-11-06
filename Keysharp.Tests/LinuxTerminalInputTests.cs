#if LINUX
using System.Buffers.Binary;

namespace Keysharp.Tests;

[TestFixture, Category("Curated")]
public class LinuxTerminalInputTests : TestRunner
{
	[Test]
	public void ControlSendTextTargets() => Assert.That(TestScript("control-send-text-tty", true), Is.True);
}

[TestFixture, Category("Internal"), Category("Curated")]
public class LinuxTerminalInputProtocolTests
{
	[Test]
	public void TextBytes()
	{
		Assert.That(LinuxTerminalInput.EncodeText("Aé😀\t\b\0{Enter}"), Is.EqualTo(new byte[] {
			0x41, 0xc3, 0xa9, 0xf0, 0x9f, 0x98, 0x80, 0x09, 0x08, 0x00,
			0x7b, 0x45, 0x6e, 0x74, 0x65, 0x72, 0x7d }));
		Assert.That(LinuxTerminalInput.EncodeText("one\r\ntwo\nthree\rfour\n\r\n"),
			Is.EqualTo(Encoding.ASCII.GetBytes("one\rtwo\rthree\rfour\r\r")));
		Assert.That(LinuxTerminalInput.EncodeText(""), Is.Empty);
	}

	[Test]
	public void EncodedByteLimit()
	{
		const int limit = KeysharpInputClient.MaxTerminalInputBytes;
		Assert.That(LinuxTerminalInput.EncodeText(new string('a', limit)), Has.Length.EqualTo(limit));
		var multibyte = LinuxTerminalInput.EncodeText(new string('a', limit - 6) + "é😀");
		Assert.That(multibyte, Has.Length.EqualTo(limit));
		Assert.That(multibyte[^6..], Is.EqualTo(new byte[] { 0xc3, 0xa9, 0xf0, 0x9f, 0x98, 0x80 }));
		Assert.Throws<ArgumentException>(() => LinuxTerminalInput.EncodeText(new string('a', limit - 5) + "é😀"));
		Assert.That(LinuxTerminalInput.EncodeText(string.Concat(Enumerable.Repeat("\r\n", limit))),
			Is.EqualTo(Enumerable.Repeat((byte)13, limit).ToArray()));
		Assert.Throws<ArgumentException>(() => LinuxTerminalInput.EncodeText(string.Concat(Enumerable.Repeat("\r\n", limit + 1))));
	}

	[Test]
	public void TerminalPermissionScope()
	{
		Assert.That(KeysharpInputClient.RequiredScopes(KeysharpInputClient.Operations.TerminalInput),
			Is.EqualTo(LinuxPermissionScope.InputControl));
		Assert.That(KeysharpInputClient.RequiredScopes(KeysharpInputClient.Operations.TerminalInput
			| KeysharpInputClient.Operations.HookKeyboard),
			Is.EqualTo(LinuxPermissionScope.InputControl | LinuxPermissionScope.InputMonitoring));
	}

	[Test]
	public void NativeStructMirrors()
	{
		var library = RequireTerminalLibrary();
		try
		{
			AssertNativeSize("NativeTerminalInputOptions", NativeLibrary.GetExport(library, "ksi_terminal_input_options_init"));
			AssertNativeSize("NativeTerminalInputResult", NativeLibrary.GetExport(library, "ksi_terminal_input_result_init"));
		}
		finally { NativeLibrary.Free(library); }
	}

	[TestCase("Revoked")]
	[TestCase("Disconnected")]
	[TestCase("Timeout")]
	[TestCase("Malformed")]
	public void NativeTerminalRequests(string failure)
	{
		var library = RequireTerminalLibrary();
		var directory = Directory.CreateTempSubdirectory("keysharp-terminal-");
		try
		{
			using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
			var socketPath = Path.Combine(directory.FullName, "socket");
			listener.Bind(new UnixDomainSocketEndPoint(socketPath));
			listener.Listen(1);
			byte[] bytes = [0x41, 0xc3, 0xa9, 0, 0xf0, 0x9f, 0x98, 0x80, 0x08, 0x0d];
			var server = Task.Run(() =>
			{
				Assert.That(listener.Poll(TimeSpan.FromSeconds(2), SelectMode.SelectRead), Is.True);
				using var peer = listener.Accept();
				using var stream = new NetworkStream(peer) { ReadTimeout = 2_000, WriteTimeout = 2_000 };
				var hello = ReadRequest(stream, 0x0001);
				Assert.That(hello.Body, Has.Length.EqualTo(16));
				Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(hello.Body), Is.Zero);
				Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(hello.Body.AsSpan(2)), Is.Zero);
				Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(hello.Body.AsSpan(4)),
					Is.EqualTo((uint)LinuxPermissionScope.InputControl));
				var greeting = new byte[32];
				BinaryPrimitives.WriteUInt32LittleEndian(greeting.AsSpan(8), (uint)LinuxPermissionScope.InputControl);
				BinaryPrimitives.WriteUInt64LittleEndian(greeting.AsSpan(16), (ulong)KeysharpInputClient.Operations.TerminalInput);
				WriteResponse(stream, 0x0001, hello.Id, greeting);

				for (var index = 0; index < 2; index++)
				{
					var request = ReadRequest(stream, 0x1013);
					Assert.That(request.Body, Has.Length.EqualTo(16 + bytes.Length));
					Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(request.Body), Is.EqualTo(1u));
					Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(request.Body.AsSpan(4)), Is.EqualTo(3u));
					Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(request.Body.AsSpan(8)), Is.EqualTo(3u));
					Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(request.Body.AsSpan(12)), Is.EqualTo((uint)bytes.Length));
					Assert.That(request.Body[16..], Is.EqualTo(bytes));
					if (index == 1 && failure == "Disconnected")
						return;
					if (index == 1 && failure == "Timeout")
						break; // Withhold the response until the client closes its timed-out connection.
					var response = new byte[16];
					BinaryPrimitives.WriteUInt32LittleEndian(response.AsSpan(8), index == 0 ? (uint)bytes.Length : 2u);
					if (index == 1)
					{
						if (failure == "Malformed")
							BinaryPrimitives.WriteUInt32LittleEndian(response.AsSpan(8), (uint)bytes.Length + 1u);
						else
						{
							BinaryPrimitives.WriteUInt32LittleEndian(response, (uint)NativeClientStatus.Revoked);
							BinaryPrimitives.WriteUInt32LittleEndian(response.AsSpan(12), 125u); // Linux ECANCELED.
						}
					}
					WriteResponse(stream, 0x1013, request.Id, response);
				}
				Assert.That(stream.ReadByte(), Is.EqualTo(-1), "A partial terminal send must not be replayed.");
			});
			try
			{
				using var client = KeysharpInputClient.Connect(KeysharpInputClient.Operations.TerminalInput,
					socketPath, requestTimeoutMs: failure == "Timeout" ? 500 : 2_000, role: KeysharpInputClient.ConnectionRole.Rpc);
				client.SendTerminalInput(3, bytes);
				var error = Assert.Throws<NativeClientException>(() => client.SendTerminalInput(3, bytes));
				var status = failure switch
				{
					"Revoked" => NativeClientStatus.Revoked,
					"Disconnected" => NativeClientStatus.Unavailable,
					"Timeout" => NativeClientStatus.Timeout,
					_ => NativeClientStatus.Internal,
				};
				Assert.That(error.Status, Is.EqualTo(status));
				if (failure == "Revoked")
					Assert.That(error.SystemError, Is.EqualTo(125));
				Assert.That(error.Message, Does.Contain($"Confirmed submitted bytes: {(failure == "Revoked" ? 2 : 0)} of 10")
					.And.Contain("not retried"));
				if (failure != "Revoked")
					Assert.That(error.Message, Does.Contain("Delivery of the remaining bytes is unknown"));
				if (failure == "Timeout")
					Assert.That(client.IsConnected, Is.False);
			}
			finally { server.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult(); }
			Assert.That(listener.Poll(TimeSpan.Zero, SelectMode.SelectRead), Is.False, "A partial send must not reconnect.");
		}
		finally
		{
			directory.Delete(true);
			NativeLibrary.Free(library);
		}
	}

	private static (ulong Id, byte[] Body) ReadRequest(NetworkStream stream, ushort opcode)
	{
		Span<byte> header = stackalloc byte[24];
		stream.ReadExactly(header);
		Assert.That(header[..4].ToArray(), Is.EqualTo("KSIP"u8.ToArray()));
		Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(header[4..]), Is.EqualTo(3));
		Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(header[8..]), Is.EqualTo(opcode));
		Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(header[10..]), Is.Zero);
		var size = BinaryPrimitives.ReadUInt32LittleEndian(header[12..]);
		Assert.That(size, Is.LessThanOrEqualTo(1040u));
		var body = new byte[checked((int)size)];
		stream.ReadExactly(body);
		return (BinaryPrimitives.ReadUInt64LittleEndian(header[16..]), body);
	}

	private static void WriteResponse(NetworkStream stream, ushort opcode, ulong id, byte[] body)
	{
		Span<byte> header = stackalloc byte[24];
		header.Clear();
		"KSIP"u8.CopyTo(header);
		BinaryPrimitives.WriteUInt16LittleEndian(header[4..], 3);
		BinaryPrimitives.WriteUInt16LittleEndian(header[8..], opcode);
		BinaryPrimitives.WriteUInt16LittleEndian(header[10..], 1);
		BinaryPrimitives.WriteUInt32LittleEndian(header[12..], (uint)body.Length);
		BinaryPrimitives.WriteUInt64LittleEndian(header[16..], id);
		stream.Write(header);
		stream.Write(body);
	}

	private static nint RequireTerminalLibrary()
	{
		if (!NativeLibrary.TryLoad("libkeysharp-input.so.1", typeof(KeysharpInputClient).Assembly, null, out var library))
			Assert.Ignore("libkeysharp-input.so.1 is not installed.");
		foreach (var symbol in new[] { "ksi_terminal_input", "ksi_terminal_input_options_init", "ksi_terminal_input_result_init" })
		{
			if (NativeLibrary.TryGetExport(library, symbol, out _)) continue;
			NativeLibrary.Free(library);
			Assert.Ignore("The installed keysharp-input client predates terminal input.");
		}
		return library;
	}

	private static void AssertNativeSize(string name, nint initializer)
	{
		// The native initializer writes its own size, so it needs room beyond the managed mirror.
		var buffer = Marshal.AllocHGlobal(8192);
		try
		{
			Marshal.GetDelegateForFunctionPointer<NativeInitializer>(initializer)(buffer);
			var mirror = typeof(KeysharpInputClient).GetNestedType(name, BindingFlags.NonPublic);
			Assert.That(mirror, Is.Not.Null);
			Assert.That(Marshal.SizeOf(mirror), Is.EqualTo(Marshal.ReadInt32(buffer)),
				$"{name} does not match the installed native client.");
		}
		finally { Marshal.FreeHGlobal(buffer); }
	}

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate void NativeInitializer(nint value);
}
#endif
