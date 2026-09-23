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

			/// <summary>Runs an executable directly, without a command shell, and captures both output streams.</summary>
			internal static CommandResult RunCommand(string fileName, params string[] arguments)
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

				try
				{
					// Both streams are drained concurrently before waiting, or a child that fills one pipe's
					// buffer would block forever while we wait for it to exit.
					var outputTask = process.StandardOutput.ReadToEndAsync();
					var errorTask = process.StandardError.ReadToEndAsync();
					process.WaitForExit();
					return new(process.ExitCode, outputTask.GetAwaiter().GetResult(), errorTask.GetAwaiter().GetResult());
				}
				catch (Exception ex)
				{
					return new(-1, string.Empty, ex.Message);
				}
			}

#if WINDOWS
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

			internal static unsafe long? GetParentProcessId(int pid)
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
						// AutoHotkey reports the recorded parent even if its PID was reused.
						if (entry.ProcessId == (uint)pid)
							return entry.ParentProcessId;

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

			public static uint CurrentThreadId() => (uint)Keysharp.Internals.Window.Linux.X11.Xlib.gettid();

			public static bool DestroyIcon(nint icon) => Keysharp.Internals.Window.Linux.X11.Xlib.GdipDisposeImage(icon) == 0;
#endif
		}
	}
}
