using System.Buffers.Binary;
using Keysharp.Builtins;
using ExitReasons = Keysharp.Builtins.Flow.ExitReasons;

namespace Keysharp.Internals.Scripting
{
	/// <summary>
	/// Each script listens on a PID-named local socket in a per-user folder. A connection carries one request and its
	/// reply, each a length-prefixed frame. Listing the folder finds scripts for messaging and instance management.
	/// </summary>
	internal sealed partial class ScriptEndpoint : IDisposable
	{
		internal const byte Info = 1, Text = 2, Binary = 3, Exit = 4;
		internal const byte Empty = 0, Accepted = 1;
		/// <summary>The largest message, in bytes.</summary>
		internal const int MaxPayload = 64 << 20;
		internal const byte Version = 3;
		// A message frame also carries its sender's path.
		private const int MaxFrame = MaxPayload + (1 << 17);
		// An info request is answered without the script's own thread, so a live script answers at once.
		internal const int QueryTimeoutMs = 1000;

		private static string folder;
		internal static readonly long CurrentProcessStartTicks = ProcessStartTicks(Environment.ProcessId);

		private readonly Script script;
		private readonly Socket listener;
		private readonly string socketPath;
		private readonly long processStartTicks = CurrentProcessStartTicks;
		private readonly CancellationTokenSource stopping = new();

		internal readonly record struct Peer(int Pid, string FullPath, long ProcessStartTicks, string ScriptPath);

		private ScriptEndpoint(Script script, Socket listener, string socketPath)
		{
			this.script = script;
			this.listener = listener;
			this.socketPath = socketPath;
		}

		/// <summary>The folder holding every running script's socket.</summary>
		private static string Folder => folder ??= ResolveFolder();

		internal static long ProcessStartTicks(long pid)
		{
			if (pid <= 0 || pid > int.MaxValue)
				return 0;

			try
			{
#if LINUX
				// The raw kernel counter is stable across processes; .NET's boot-to-wall-clock conversion is not.
				var stat = File.ReadAllText(FormattableString.Invariant($"/proc/{pid}/stat"));
				var fields = stat.AsSpan(stat.LastIndexOf(')') + 1).Trim();
				var column = 3;

				foreach (var range in fields.Split(' '))
					if (!fields[range].IsEmpty && column++ == 22)
						return long.TryParse(fields[range], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) ? ticks : 0;

				return 0;
#else
				using var process = Process.GetProcessById((int)pid);
				return process.StartTime.ToUniversalTime().Ticks;
#endif
			}
			catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException
				or InvalidOperationException or System.ComponentModel.Win32Exception)
			{
				return 0;
			}
		}

		private static string ResolveFolder()
		{
#if WINDOWS
			// One folder per logon session, as each session has its own desktop. Process.SessionId would enumerate every
			// process on the machine, and GetFolderPath loads the shell, each costing every script's start milliseconds.
			_ = WindowsAPI.ProcessIdToSessionId((uint)Environment.ProcessId, out var session);
			var local = Environment.GetEnvironmentVariable("LOCALAPPDATA");

			if (string.IsNullOrEmpty(local))
				local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

			return Path.Combine(local, "Keysharp", "Scripts", session.ToString(CultureInfo.InvariantCulture));
#elif LINUX
			var runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
			return string.IsNullOrEmpty(runtime) ? Path.Combine(Path.GetTempPath(), $"keysharp-{Keysharp.Internals.Window.Linux.X11.Xlib.geteuid()}")
				   : Path.Combine(runtime, "keysharp");
#else
			// macOS gives each user a temporary folder of their own.
			return Path.Combine(Path.GetTempPath(), "keysharp");
#endif
		}

		internal static string PathFor(long pid) => Path.Combine(Folder, pid.ToString(CultureInfo.InvariantCulture));

		/// <summary>Starts listening for <paramref name="script"/>, or returns null when it cannot, which leaves the script
		/// unreachable but otherwise unaffected.</summary>
		internal static ScriptEndpoint Start(Script script)
		{
			var path = PathFor(Environment.ProcessId);
			Socket socket = null;

			try
			{
#if WINDOWS
				_ = Directory.CreateDirectory(Folder);
#else
				if (!OperatingSystem.IsWindows())
				{
					const UnixFileMode permissions = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
					_ = Directory.CreateDirectory(Folder, permissions);

					// Existing fallback directories must be owned by this user and private too.
					if ((File.GetAttributes(Folder) & FileAttributes.ReparsePoint) != 0)
						throw new UnauthorizedAccessException("The script endpoint folder cannot be a symbolic link.");

					File.SetUnixFileMode(Folder, permissions);
				}
#endif
				// A socket left by an earlier process with this PID, or by an earlier script of this one.
				File.Delete(path);
				socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
				socket.Bind(new UnixDomainSocketEndPoint(path));
				socket.Listen();
				var endpoint = new ScriptEndpoint(script, socket, path);
				_ = endpoint.AcceptAsync();
				return endpoint;
			}
			catch (Exception ex) when (ex is IOException or SocketException or UnauthorizedAccessException or ArgumentException)
			{
				socket?.Dispose();
				Diagnostics.Debug.WriteLine($"Keysharp: other scripts cannot reach this one: {ex.Message}");
				return null;
			}
		}

		public void Dispose()
		{
			stopping.Cancel();
			listener.Dispose();

			try
			{
				File.Delete(socketPath);
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
			}
		}

		// ---- serving ---------------------------------------------------------------------------------------

		private async Task AcceptAsync()
		{
			try
			{
				while (true)
					_ = ServeAsync(await listener.AcceptAsync(stopping.Token).ConfigureAwait(false));
			}
			catch (Exception ex) when (ex is SocketException or ObjectDisposedException or OperationCanceledException)
			{
				// Disposed: this script is exiting.
			}
		}

		private async Task ServeAsync(Socket client)
		{
			try
			{
				using (client)
				{
					await using var stream = new NetworkStream(client, ownsSocket: false);
					var request = await ReadFrameAsync(stream, stopping.Token).ConfigureAwait(false);

					if (request.Length < 2 || request[0] != Version)
						throw new InvalidDataException("Unknown request.");

					if (request[1] != Info && (request.Length < 10 || processStartTicks == 0
						|| BinaryPrimitives.ReadInt64LittleEndian(request.AsSpan(2)) != processStartTicks))
						throw new InvalidDataException("The script instance has ended.");

					// Keep delivery alive between owners and until the reply is written.
					var isMessage = request[1] is Text or Binary;
					using var pending = isMessage
						? script.MainEventScheduler.RegisterPendingCallback(client.Dispose) : null;

					if (isMessage && pending == null)
						return;

					byte[] reply;
					var reason = ExitReasons.Close;
					var reloadToken = 0;

					switch (request[1])
					{
						case Info:
						{
							using var buffer = new MemoryStream();

							using (var writer = new BinaryWriter(buffer, Encoding.UTF8, true))
							{
								writer.Write(Accessors.A_ScriptFullPath ?? "");
								writer.Write(processStartTicks);
								writer.Write(script.scriptPath ?? "");
							}

							reply = buffer.ToArray();
							break;
						}

						case Text:
						case Binary:
						{
							using var reader = new BinaryReader(new MemoryStream(request, 10, request.Length - 10), Encoding.UTF8);
							var senderPid = reader.ReadInt32();
							var senderProcessStartTicks = reader.ReadInt64();
							var senderPath = reader.ReadString();
							var payload = request.AsMemory(10 + (int)reader.BaseStream.Position);

							if (payload.Length > MaxPayload)
								throw new InvalidDataException("The message is too large.");

							reply = await script.ScriptMessageManager.DispatchAsync(request[1], payload, senderPid, senderPath, senderProcessStartTicks)
								.WaitAsync(stopping.Token).ConfigureAwait(false);
							break;
						}

						case Exit when request.Length == 15:
							reason = (ExitReasons)request[10];
							reloadToken = BinaryPrimitives.ReadInt32LittleEndian(request.AsSpan(11));
							reply = [reason is ExitReasons.Single or ExitReasons.Close
								|| reason == ExitReasons.Reload && script.FlowData.pendingReload is { } handshake
								&& handshake.RequestToken == reloadToken ? Accepted : Empty];
							break;

						default:
							throw new InvalidDataException("Unknown request.");
					}

					await WriteFrameAsync(stream, reply, stopping.Token).ConfigureAwait(false);

					// Exit tears down this endpoint, so acknowledge the request before queuing it.
					if (request[1] == Exit && reply is [Accepted])
					{
						if (reason == ExitReasons.Reload)
							_ = script.FlowData.pendingReload?.RequestRetire(reloadToken);
						else
							_ = script.MainEventScheduler.EnqueueCallback(() =>
							{
								if (!script.hasExited && script.FlowData.exitReason == null)
									_ = Keysharp.Internals.Flow.ExitAppInternal(script, reason, null, false);
							}, ScriptEventQueue.Interactive);
					}
				}
			}
			catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or OperationCanceledException
										   or FormatException)
			{
				// A client that gave up or sent nonsense, or this script exiting.
			}
		}

		// ---- finding scripts -------------------------------------------------------------------------------

		/// <summary>Another instance with the same raw script path, oldest first. Windows peers with a main window use
		/// its title instead, so a changed title or a different runtime version retains AHK's instance behavior.</summary>
		internal static Peer? FindOtherInstance(string scriptPath)
		{
			var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

			foreach (var peer in Running())
			{
				if (peer.Pid == Environment.ProcessId || !string.Equals(peer.ScriptPath, scriptPath, comparison))
					continue;
#if WINDOWS
				if (MainWindowOf(peer.Pid) != 0)
					continue;
#endif
				return peer;
			}

			return null;
		}

		/// <summary>The scripts answering now, oldest first. Queries run together with a bounded wait.</summary>
		internal static List<Peer> Running()
		{
			var queries = new List<Task<Peer?>>();

			try
			{
				foreach (var file in Directory.GetFiles(Folder))
					if (int.TryParse(Path.GetFileName(file.AsSpan()), NumberStyles.None, CultureInfo.InvariantCulture, out var pid) && pid > 0)
						queries.Add(QueryAsync(pid));
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				return [];
			}

			var peers = new List<Peer>(queries.Count);

			if (queries.Count == 0)
				return peers;

			// Each query has its own timeout, and none waits on a script thread, so this cannot block for long.
			Task.WaitAll(queries);

			foreach (var query in queries)
				if (query.Result is { } peer)
					peers.Add(peer);

			peers.Sort(static (a, b) => a.ProcessStartTicks.CompareTo(b.ProcessStartTicks));
			return peers;
		}

		/// <summary>The script currently answering at a PID's endpoint, without waiting for startup.</summary>
		internal static async Task<Peer?> QueryAsync(int pid)
		{
			if (pid <= 0)
				return null;

			using var timeout = new CancellationTokenSource(QueryTimeoutMs);

			try
			{
				return DecodeInfo(pid, await ExchangeAsync(pid, [Version, Info], timeout.Token).ConfigureAwait(false));
			}
			catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or FormatException
				or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
			{
				return null;
			}
		}

		internal static Peer DecodeInfo(int pid, byte[] reply)
		{
			using var reader = new BinaryReader(new MemoryStream(reply), Encoding.UTF8);
			var peer = new Peer(pid, reader.ReadString(), reader.ReadInt64(), reader.ReadString());

			if (peer.ProcessStartTicks <= 0 || reader.BaseStream.Position != reader.BaseStream.Length)
				throw new InvalidDataException("Invalid script information.");

			return peer;
		}

		// ---- requests --------------------------------------------------------------------------------------

		/// <summary>A message request from the current script.</summary>
		internal static byte[] MessageRequest(bool binary, int payloadLength, long expectedProcessStartTicks)
		{
			using var header = new MemoryStream();
			using var writer = new BinaryWriter(header, Encoding.UTF8);
			writer.Write(Version);
			writer.Write(binary ? Binary : Text);
			writer.Write(expectedProcessStartTicks);
			writer.Write(Environment.ProcessId);
			writer.Write(CurrentProcessStartTicks);
			writer.Write(Accessors.A_ScriptFullPath ?? "");
			var frame = new byte[(int)header.Length + payloadLength];
			header.GetBuffer().AsSpan(0, (int)header.Length).CopyTo(frame);
			return frame;
		}

		/// <summary>An exit request, which the target serves as AHK serves a request to close its main window.</summary>
		internal static byte[] ExitRequest(ExitReasons reason, long expectedProcessStartTicks, int reloadToken = 0)
		{
			var request = new byte[15];
			request[0] = Version;
			request[1] = Exit;
			BinaryPrimitives.WriteInt64LittleEndian(request.AsSpan(2), expectedProcessStartTicks);
			request[10] = (byte)reason;
			BinaryPrimitives.WriteInt32LittleEndian(request.AsSpan(11), reloadToken);
			return request;
		}

		/// <summary>Sends one request once the original process lifetime listens, or throws when it ends first.</summary>
		internal static async Task<byte[]> RequestAsync(long pid, long expectedProcessStartTicks, byte[] request, CancellationToken token = default)
		{
			if (pid <= 0 || pid > int.MaxValue || expectedProcessStartTicks <= 0)
				throw new IOException($"The script (PID {pid}) is not running.");

			var retryDelay = 10;

			while (true)
			{
				token.ThrowIfCancellationRequested();

				try
				{
					return await ExchangeAsync(pid, request, token).ConfigureAwait(false);
				}
				catch (SocketException ex) when (ex.SocketErrorCode is SocketError.ConnectionRefused or SocketError.AddressNotAvailable)
				{
					// The child has not bound its socket yet, or has bound it but not started listening.
				}

				if (ProcessStartTicks(pid) != expectedProcessStartTicks)
					throw new IOException($"The script (PID {pid}) is not running.");

				await Task.Delay(retryDelay, token).ConfigureAwait(false);
				retryDelay = Math.Min(100, retryDelay * 2);
			}
		}

		private static async Task<byte[]> ExchangeAsync(long pid, byte[] request, CancellationToken token)
		{
			using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
			await socket.ConnectAsync(new UnixDomainSocketEndPoint(PathFor(pid)), token).ConfigureAwait(false);
			await using var stream = new NetworkStream(socket, ownsSocket: false);
			await WriteFrameAsync(stream, request, token).ConfigureAwait(false);
			return await ReadFrameAsync(stream, token).ConfigureAwait(false);
		}

		private static async Task<byte[]> ReadFrameAsync(Stream stream, CancellationToken token)
		{
			var header = new byte[sizeof(int)];
			await stream.ReadExactlyAsync(header, token).ConfigureAwait(false);
			var length = BinaryPrimitives.ReadInt32LittleEndian(header);

			if ((uint)length > MaxFrame)
				throw new InvalidDataException("The frame is too large.");

			var body = new byte[length];
			await stream.ReadExactlyAsync(body, token).ConfigureAwait(false);
			return body;
		}

		private static async Task WriteFrameAsync(Stream stream, byte[] body, CancellationToken token)
		{
			var header = new byte[sizeof(int)];
			BinaryPrimitives.WriteInt32LittleEndian(header, body.Length);
			await stream.WriteAsync(header, token).ConfigureAwait(false);
			await stream.WriteAsync(body, token).ConfigureAwait(false);
		}
	}
}
