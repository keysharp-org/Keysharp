#if WINDOWS

namespace Keysharp.Main;

internal static class CompileDaemonSecurity
{
	internal static readonly string AccountId;
	internal static readonly bool IsElevated;
	private const int CreateNoWindow = 0x08000000;
	private const int CreateUnicodeEnvironment = 0x400;
	private const int StartUseShowWindow = 1;
	private const int TokenElevation = 20;
	private const int TokenLinkedToken = 19;
	private const int TokenQuery = 8;
	static CompileDaemonSecurity()
	{
		using var identity = WindowsIdentity.GetCurrent();
		AccountId = identity.User.Value;
		IsElevated = IsElevatedToken(identity.AccessToken);
	}

	internal static bool HasDaemonArguments(string commandLine, string image, string entryAssembly)
	{
		if (string.IsNullOrEmpty(commandLine))
			return false;
		var arguments = CommandLineToArgvW(commandLine, out var count);
		if (arguments == 0)
			return false;
		try
		{
			var hosted = Path.GetFileNameWithoutExtension(image).Equals("dotnet", StringComparison.OrdinalIgnoreCase);
			return count == (hosted ? 3 : 2) && (!hosted || string.Equals(Marshal.PtrToStringUni(Marshal.ReadIntPtr(arguments, IntPtr.Size)), entryAssembly, StringComparison.OrdinalIgnoreCase)) && string.Equals(Marshal.PtrToStringUni(Marshal.ReadIntPtr(arguments, (count - 1) * IntPtr.Size)), "--daemon", StringComparison.OrdinalIgnoreCase);
		}
		finally
		{
			_ = LocalFree(arguments);
		}
	}

	internal static bool IsExpectedServer(NamedPipeClientStream pipe)
	{
		if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var pid)
			|| !DaemonCoordinator.HasLiveOwner(CompileServer.PipeName, (int)pid))
			return false;

		using var process = Process.GetProcessById((int)pid);
		if (!OpenProcessToken(process.Handle, TokenQuery, out var token))
			return false;
		using (token)
		using (var identity = new WindowsIdentity(token.DangerousGetHandle()))
		{
			if (identity.User.Value != AccountId || IsElevatedToken(token))
				return false;
		}

		var image = Processes.GetProcessImage(pid, false);
		return string.Equals(image, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase)
			&& HasDaemonArguments(ReadCommandLine(process), image, Assembly.GetEntryAssembly()?.Location);
	}

	internal static unsafe string ReadCommandLine(Process process)
	{
		if (NtQueryInformationProcess(process.Handle, 0, out var basic, Marshal.SizeOf<ProcessBasicInformation>(), out _) != 0
			|| !ReadStructure(process.Handle, basic.Peb, out Peb peb)
			|| !ReadStructure(process.Handle, peb.Parameters, out ProcessParameters parameters))
			return null;
		var commandLine = parameters.CommandLine;
		if (commandLine.Length == 0 || commandLine.Length % 2 != 0 || commandLine.Buffer == 0)
			return null;
		var bytes = new byte[commandLine.Length];
		fixed (byte* buffer = bytes)
			return ReadProcessMemory(process.Handle, commandLine.Buffer, (nint)buffer, (nuint)bytes.Length, out var read)
				&& read == (nuint)bytes.Length ? Encoding.Unicode.GetString(bytes) : null;
	}

	internal static unsafe bool TryStartUnelevated(ProcessStartInfo startInfo)
	{
		// The linked-token path starts an apphost with one fixed argument; hosted launches compile locally.
		if (startInfo.ArgumentList.Count != 1 || startInfo.ArgumentList[0] != "--daemon"
			|| Path.GetFileNameWithoutExtension(startInfo.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
			return false;

		using var identity = WindowsIdentity.GetCurrent();
		if (!GetTokenInformation(identity.AccessToken, TokenLinkedToken, out nint linked, IntPtr.Size, out _))
			return false;
		using var token = new SafeAccessTokenHandle(linked);
		using var normalIdentity = new WindowsIdentity(linked);
		// Credential elevation can belong to a different account from the interactive shell.
		if (normalIdentity.User != identity.User || IsElevatedToken(token))
			return false;

		var commandLine = new StringBuilder($"\"{startInfo.FileName}\" --daemon");
		if (commandLine.Length >= 1024)
			return false;

		fixed (char* environment = string.Join('\0', startInfo.Environment
			.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase).Select(pair => $"{pair.Key}={pair.Value}")) + "\0\0")
		{
			var startup = new StartupInfo { Size = Marshal.SizeOf<StartupInfo>(), Flags = StartUseShowWindow, ShowWindow = 0 };
			if (!CreateProcessWithTokenW(token, 0, startInfo.FileName, commandLine, CreateNoWindow | CreateUnicodeEnvironment,
				(nint)environment, startInfo.WorkingDirectory, ref startup, out var process))
				return false;
			_ = WindowsAPI.CloseHandle(process.Thread);
			_ = WindowsAPI.CloseHandle(process.Process);
			return true;
		}
	}

	[DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern nint CommandLineToArgvW(string commandLine, out int count);

	[DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern bool CreateProcessWithTokenW(SafeAccessTokenHandle token, int logonFlags, string application,
		StringBuilder commandLine, int creationFlags, nint environment, string directory,
		ref StartupInfo startup, out ProcessInformation process);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);

	[DllImport("advapi32.dll", SetLastError = true)]
	private static extern bool GetTokenInformation(SafeAccessTokenHandle token, int informationClass,
		out int information, int informationLength, out int returnLength);

	[DllImport("advapi32.dll", SetLastError = true)]
	private static extern bool GetTokenInformation(SafeAccessTokenHandle token, int informationClass,
		out nint information, int informationLength, out int returnLength);

	private static bool IsElevatedToken(SafeAccessTokenHandle token) =>
											!GetTokenInformation(token, TokenElevation, out int elevated, sizeof(int), out _) || elevated != 0;
	[DllImport("kernel32.dll")]
	private static extern nint LocalFree(nint allocation);

	[DllImport("ntdll.dll")]
	private static extern int NtQueryInformationProcess(nint process, int informationClass,
		out ProcessBasicInformation information, int length, out int returnLength);

	[DllImport("advapi32.dll", SetLastError = true)]
	private static extern bool OpenProcessToken(nint process, int access, out SafeAccessTokenHandle token);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool ReadProcessMemory(nint process, nint address, nint buffer, nuint size, out nuint read);

	private static unsafe bool ReadStructure<T>(nint process, nint address, out T value) where T : unmanaged
	{
		T buffer = default;
		var success = address != 0 && ReadProcessMemory(process, address, (nint)(&buffer), (nuint)sizeof(T), out var read) && read == (nuint)sizeof(T);
		value = buffer;
		return success;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct Peb
	{
		internal int Flags;
		internal nint Reserved1, Reserved2, Loader, Parameters;
	}

	// Published winternl layouts; the authenticated image path ensures the server has this process's bitness.
	[StructLayout(LayoutKind.Sequential)]
	private struct ProcessBasicInformation
	{
		internal nint Reserved1, Peb, Reserved2, Reserved3, ProcessId, Reserved4;
	}
	[StructLayout(LayoutKind.Sequential)]
	private struct ProcessInformation
	{
		internal nint Process, Thread;
		internal int ProcessId, ThreadId;
	}

	[StructLayout(LayoutKind.Sequential)]
	private unsafe struct ProcessParameters
	{
		internal fixed byte Reserved1[16];
		internal nint Reserved2, Reserved3, Reserved4, Reserved5, Reserved6, Reserved7, Reserved8, Reserved9, Reserved10, Reserved11;
		internal UnicodeString Image, CommandLine;
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct StartupInfo
	{
		internal int Size;
		internal string Reserved, Desktop, Title;
		internal int X, Y, Width, Height, CharacterWidth, CharacterHeight, FillAttribute, Flags;
		internal short ShowWindow, ReservedSize;
		internal nint ReservedBytes, StandardInput, StandardOutput, StandardError;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct UnicodeString
	{
		internal ushort Length, MaximumLength;
		internal nint Buffer;
	}
}
#endif
