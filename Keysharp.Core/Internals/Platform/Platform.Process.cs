namespace Keysharp.Internals
{
	internal static partial class Platform
	{
		/// <summary>Process/thread + icon-resource primitives. Compile-time per-OS.</summary>
		internal static partial class Process
		{
			/// <summary><paramref name="Started"/> separates "the executable isn't there" from "it ran and failed",
			/// which callers on optional helper tools need to tell apart.</summary>
			internal readonly record struct CommandResult(int ExitCode, string StandardOutput, string StandardError, bool Started = true)
			{
				internal bool Succeeded => Started && ExitCode == 0;

				internal string ErrorMessage => !string.IsNullOrWhiteSpace(StandardError)
					? StandardError.Trim()
					: !string.IsNullOrWhiteSpace(StandardOutput)
						? StandardOutput.Trim()
						: $"Process exited with code {ExitCode}.";
			}

			/// <summary>
			/// Runs an executable directly, without a command shell, and captures both output streams. The timeout
			/// covers the exit and both reads; a helper still running then is killed with its child processes and
			/// reported as failed. The default, -1, waits indefinitely.
			/// </summary>
			internal static CommandResult RunCommand(string fileName, string[] arguments, int timeoutMs = -1)
			{
				using var process = new System.Diagnostics.Process
				{
					StartInfo = new ProcessStartInfo
					{
						FileName = fileName,
						RedirectStandardOutput = true,
						RedirectStandardError = true,
						UseShellExecute = false,
						CreateNoWindow = true
					}
				};

				foreach (var argument in arguments)
					process.StartInfo.ArgumentList.Add(argument);

				try
				{
					if (!process.Start())
						return new(-1, string.Empty, $"Failed to start {fileName}.", false);
				}
				catch (Exception ex)   // Win32Exception when the executable isn't installed, and friends.
				{
					return new(-1, string.Empty, ex.Message, false);
				}

				using var cancellation = new CancellationTokenSource();
				Task completion = null;

				try
				{
					// Both streams are drained concurrently before waiting, or a child that fills one pipe's
					// buffer would block forever while we wait for it to exit.
					var outputTask = process.StandardOutput.ReadToEndAsync(cancellation.Token);
					var errorTask = process.StandardError.ReadToEndAsync(cancellation.Token);
					completion = Task.WhenAll(process.WaitForExitAsync(cancellation.Token), outputTask, errorTask);

					if (!Flow.WaitForCompletion(completion, timeoutMs))
					{
						try
						{
							if (!process.HasExited)
								process.Kill(entireProcessTree: true);
						}
						catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { }

						return new(-1, string.Empty, $"{fileName} did not finish within {timeoutMs / 1000.0} s.");
					}

					completion.GetAwaiter().GetResult();
					return new(process.ExitCode, outputTask.GetAwaiter().GetResult(), errorTask.GetAwaiter().GetResult());
				}
				catch (Exception ex) when (!Flow.TryGetException<Keysharp.Builtins.Flow.UserRequestedExitException>(ex, out _))
				{
					return new(-1, string.Empty, ex.Message);
				}
				finally
				{
					cancellation.Cancel();
					process.StandardOutput.Dispose();
					process.StandardError.Dispose();
					// A descendant may keep an inherited pipe open after its parent exits. Close our reads and observe
					// any late failures without waiting for that descendant to exit.
					if (completion != null)
						_ = completion.ContinueWith(static task => _ = task.Exception, CancellationToken.None,
							TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
				}
			}

#if WINDOWS
			[LibraryImport("kernel32.dll", SetLastError = true)]
			private static partial uint WaitForSingleObject(nint handle, uint milliseconds);

			internal static bool Exists(int pid)
			{
				if (pid <= 0)
					return pid == 0; // Windows always has the System Idle Process.

				var handle = Os.Windows.WindowsAPI.OpenProcess(Os.Windows.ProcessAccessTypes.SYNCHRONIZE, false, (uint)pid);
				if (handle == 0)
				{
					if (Marshal.GetLastPInvokeError() == 87) // ERROR_INVALID_PARAMETER: no such PID.
						return false;

					// A denied handle can outlive an exited process; consult the running-process snapshot.
					try { return GetParentProcessId(pid).HasValue; }
					catch (Win32Exception) { return false; }
				}

				try
				{
					return WaitForSingleObject(handle, 0) == 258; // WAIT_TIMEOUT: the process has not exited.
				}
				finally
				{
					_ = Os.Windows.WindowsAPI.CloseHandle(handle);
				}
			}

			[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
			private unsafe struct ProcessEntry32
			{
				public uint Size;
				public uint Usage;
				public uint ProcessId;
				public nuint DefaultHeapId;
				public uint ModuleId;
				public uint ThreadCount;
				public uint ParentProcessId;
				public int Priority;
				public uint Flags;
				public fixed char ExecutableFile[260];
			}

			[LibraryImport("kernel32.dll", SetLastError = true)]
			private static partial nint CreateToolhelp32Snapshot(uint flags, uint processId);

			[LibraryImport("kernel32.dll", SetLastError = true)]
			[return: MarshalAs(UnmanagedType.Bool)]
			private static partial bool Process32FirstW(nint snapshot, ref ProcessEntry32 entry);

			[LibraryImport("kernel32.dll", SetLastError = true)]
			[return: MarshalAs(UnmanagedType.Bool)]
			private static partial bool Process32NextW(nint snapshot, ref ProcessEntry32 entry);

			private delegate bool ProcessEntryMatch(ref ProcessEntry32 entry);

			// AutoHotkey reports the recorded parent even if its PID was reused.
			internal static long? GetParentProcessId(int pid)
				=> FindProcessEntry((ref ProcessEntry32 candidate) => candidate.ProcessId == (uint)pid) is { } entry ? entry.ParentProcessId : null;

			// As AutoHotkey does, the file name is compared in full, extension included.
			internal static unsafe int FindByExecutableName(string name)
			{
				try
				{
					return FindProcessEntry((ref ProcessEntry32 candidate) =>
					{
						fixed (char* file = candidate.ExecutableFile)
							return Path.GetFileName(MemoryMarshal.CreateReadOnlySpanFromNullTerminated(file)).Equals(name, StringComparison.OrdinalIgnoreCase);
					}) is { } entry ? (int)entry.ProcessId : 0;
				}
				catch (Win32Exception)
				{
					return 0;
				}
			}

			/// <summary>The first entry of a process snapshot that <paramref name="match"/> accepts, or null.</summary>
			private static unsafe ProcessEntry32? FindProcessEntry(ProcessEntryMatch match)
			{
				const uint snapProcess = 0x00000002;
				const int noMoreFiles = 18;
				var snapshot = CreateToolhelp32Snapshot(snapProcess, 0);

				if (snapshot == -1)
					throw new Win32Exception(Marshal.GetLastPInvokeError());

				try
				{
					var entry = new ProcessEntry32 { Size = (uint)sizeof(ProcessEntry32) };
					var found = Process32FirstW(snapshot, ref entry);

					while (found)
					{
						if (match(ref entry))
							return entry;

						found = Process32NextW(snapshot, ref entry);
					}

					var error = Marshal.GetLastPInvokeError();

					if (error != noMoreFiles)
						throw new Win32Exception(error);

					return null;
				}
				finally
				{
					_ = Os.Windows.WindowsAPI.CloseHandle(snapshot);
				}
			}

			/// <summary>
			/// The process's executable path or, with <paramref name="nameOnly"/>, its file name. Empty when the process
			/// cannot be queried, with the reason in the last Win32 error.
			/// </summary>
			internal static unsafe string GetProcessImage(uint pid, bool nameOnly)
			{
				var hProc = WindowsAPI.OpenProcess(ProcessAccessTypes.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);

				if (hProc == 0)
					return "";

				try
				{
					Span<char> path = stackalloc char[1024];
					while (true)
					{
						var length = (uint)path.Length;
						fixed (char* buffer = path)
						{
							// The faster device-path query suffices when only the file name is needed.
							if (nameOnly)
								length = WindowsAPI.GetProcessImageFileName(hProc, buffer, length);
							else if (!WindowsAPI.QueryFullProcessImageName(hProc, 0, buffer, ref length))
								length = 0;
						}

						if (length != 0)
						{
							var image = path[..(int)length];
							return (nameOnly ? Path.GetFileName(image) : image).ToString();
						}

						if (Marshal.GetLastPInvokeError() != 122 || path.Length == 32768) // ERROR_INSUFFICIENT_BUFFER
							return "";

						path = new char[32768];
					}
				}
				finally
				{
					_ = WindowsAPI.CloseHandle(hProc);
				}
			}

			public static uint CurrentThreadId() => Os.Windows.WindowsAPI.GetCurrentThreadId();

			public static bool DestroyIcon(nint icon) => Os.Windows.WindowsAPI.DestroyIcon(icon);
#elif OSX
			[LibraryImport("libproc.dylib", EntryPoint = "proc_pidinfo", SetLastError = true)]
			private static unsafe partial int ProcPidInfo(int pid, int flavor, ulong arg, byte* buffer, int bufferSize);

			internal static unsafe long? GetParentProcessId(int pid)
			{
				const int shortBsdInfo = 13;
				Span<byte> info = stackalloc byte[64];

				fixed (byte* buffer = info)
				{
					var size = ProcPidInfo(pid, shortBsdInfo, 0, buffer, info.Length);

					if (size == 0)
					{
						var error = Marshal.GetLastPInvokeError();

						if (error == 3)
							return null;

						throw new Win32Exception(error);
					}

					if (size < 8)
						throw new InvalidDataException("The process information is incomplete.");

					return BitConverter.ToUInt32(info.Slice(4, 4));
				}
			}

			[LibraryImport("libproc.dylib", EntryPoint = "proc_listallpids")]
			private static unsafe partial int ProcListAllPids(int* buffer, int bufferSize);

			[LibraryImport("libproc.dylib", EntryPoint = "proc_pidpath", SetLastError = true)]
			private static unsafe partial int ProcPidPath(int pid, byte* buffer, uint bufferSize);

			private static unsafe ArraySegment<int> GetProcessIds()
			{
				// Called without a buffer it reports the count, so the margin covers processes started since.
				var pids = new int[Math.Max(0, ProcListAllPids(null, 0)) + 64];
				fixed (int* buffer = pids)
				{
					var count = ProcListAllPids(buffer, pids.Length * sizeof(int));
					return new(pids, 0, Math.Clamp(count, 0, pids.Length));
				}
			}

			[LibraryImport("libSystem.dylib")]
			private static partial int pthread_threadid_np(IntPtr thread, out ulong threadid);

			public static uint CurrentThreadId()
			{
				_ = pthread_threadid_np(IntPtr.Zero, out var tid);
				return (uint)tid;
			}

			public static bool DestroyIcon(nint icon) => true;
#else
			internal static long? GetParentProcessId(int pid)
			{
				try
				{
					foreach (var line in File.ReadLines($"/proc/{pid}/status"))
					{
						if (line.StartsWith("PPid:", StringComparison.Ordinal))
						{
							if (int.TryParse(line.AsSpan(5).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var parentId))
								return parentId;

							throw new InvalidDataException("The process status contains an invalid parent process ID.");
						}
					}
				}
				catch (FileNotFoundException) { return null; }
				catch (DirectoryNotFoundException) { return null; }

				throw new InvalidDataException("The process status has no parent process ID.");
			}

			[LibraryImport("libc", EntryPoint = "readlink", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
			private static unsafe partial nint ReadLink(string path, byte* buffer, nint bufferSize);

			private static IEnumerable<int> GetProcessIds()
			{
				foreach (var dir in Directory.EnumerateDirectories("/proc"))
					if (int.TryParse(Path.GetFileName(dir.AsSpan()), NumberStyles.None, CultureInfo.InvariantCulture, out var pid))
						yield return pid;
			}

			public static uint CurrentThreadId() => (uint)Keysharp.Internals.Window.Linux.X11.Xlib.gettid();

			public static bool DestroyIcon(nint icon) => Keysharp.Internals.Window.Linux.X11.Xlib.GdipDisposeImage(icon) == 0;
#endif

#if !WINDOWS
			private const int ProcessPathCapacity = 4096; // PATH_MAX on Linux and PROC_PIDPATHINFO_MAXSIZE on macOS.

			internal static unsafe int FindByExecutableName(string name)
			{
				var path = stackalloc byte[ProcessPathCapacity];
				Span<char> chars = stackalloc char[ProcessPathCapacity];
				foreach (var pid in GetProcessIds())
				{
					var leaf = ReadProcessImage((uint)pid, true, path);
					if (leaf.IsEmpty)
						continue;

					var written = Encoding.UTF8.GetChars(leaf, chars);
					if (MemoryExtensions.Equals(chars[..written], name, StringComparison.OrdinalIgnoreCase))
						return pid;
				}
				return 0;
			}

			internal static unsafe string GetProcessImage(uint pid, bool nameOnly)
			{
				var path = stackalloc byte[ProcessPathCapacity];
				var image = ReadProcessImage(pid, nameOnly, path);
				return image.IsEmpty ? "" : Encoding.UTF8.GetString(image);
			}

			private static unsafe ReadOnlySpan<byte> ReadProcessImage(uint pid, bool nameOnly, byte* path)
			{
#if OSX
				var length = ProcPidPath((int)pid, path, ProcessPathCapacity);
#else
				// The comm name is truncated and a process can change it without changing its executable.
				var length = (int)ReadLink($"/proc/{pid}/exe", path, ProcessPathCapacity);
				if (length == ProcessPathCapacity)
				{
					Marshal.SetLastPInvokeError(36); // ENAMETOOLONG
					return [];
				}
#endif
				if (length <= 0)
					return [];

				var image = new ReadOnlySpan<byte>(path, length);
				return nameOnly ? image[(image.LastIndexOf((byte)'/') + 1)..] : image;
			}

			[LibraryImport("libc", EntryPoint = "kill", SetLastError = true)]
			private static partial int PosixKill(int pid, int signal);

			internal static bool Exists(int pid)
			{
				if (pid <= 0)
					return false;

				// Signal zero checks existence; EPERM means the process exists but belongs to another user.
				return PosixKill(pid, 0) == 0 || Marshal.GetLastPInvokeError() == 1;
			}

			[LibraryImport("libc", EntryPoint = "getuid")]
			internal static partial uint UserId();

			[LibraryImport("libc", EntryPoint = "geteuid")]
			internal static partial uint EffectiveUserId();
#endif
		}
	}
}
