namespace Keysharp.Builtins
{
	internal class ProcessesData
	{
		//internal int CurrentThreadID = Process.GetCurrentProcess().Threads[0].Id; //WindowsAPI.GetCurrentThread();
		internal string runDomain;
		internal SecureString runPassword;
		internal string runUser;
	}

	/// <summary>
	/// Public interface for process-related functions.
	/// </summary>
	public static class Processes
	{
		// As AutoHotkey's ProcessWait: listing the processes costs more than a window search, so it runs less often.
		private const int ProcessPollInterval = 100;

		private static readonly FrozenSet<string> verbs = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase)
		{
			"find",
			"explore",
			"open",
			"edit",
			"print",
			"properties"
		} .ToFrozenSet(StringComparer.InvariantCultureIgnoreCase);

		/// <summary>
		/// Forces the first matching process to close.
		/// </summary>
		/// <param name="pidOrName">
		/// Specify either a number (the PID) or a process name:<br/>
		/// PID: The Process ID, which is a number that uniquely identifies one specific process<br/>
		/// (this number is valid only during the lifetime of that process).<br/>
		/// The PID of a newly launched process can be determined via the Run function.<br/>
		/// Similarly, the PID of a window can be determined with <see cref="WinGetPID"/>.<br/>
		/// <see cref="ProcessExist"/> can also be used to discover a PID.<br/>
		/// Name: The name of a process is usually the same as its executable (without path), e.g.notepad.exe or winword.exe.<br/>
		/// Since a name might match multiple running processes, only the first process will be operated upon.<br/>
		/// The name is not case-sensitive.
		/// </param>
		/// <returns>The Process ID (PID) of the specified process. If a matching process is not found or cannot be manipulated, zero is returned.</returns>
		public static long ProcessClose([UserDeclaredName("PIDOrName")] object pidOrName)
		{
			if (!pidOrName.CoerceString(out var name)) //Will handle name string or pid int.
				return 0L;

			using (var proc = string.IsNullOrEmpty(name) ? Process.GetCurrentProcess() : FindProcess(name))
			{
				if (proc == null)
					return 0L;

				try
				{
					proc.Kill();
					return proc.Id;
				}
				catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException) { }
			}

			return 0L;
		}

		/// <summary>
		/// Checks if the specified process exists.
		/// </summary>
		/// <param name="pidOrName">
		/// Specify either a number (the PID) or a process name:<br/>
		/// PID: The Process ID, which is a number that uniquely identifies one specific process<br/>
		/// (this number is valid only during the lifetime of that process).<br/>
		/// The PID of a newly launched process can be determined via the Run function.<br/>
		/// Similarly, the PID of a window can be determined with <see cref="WinGetPID"/>.<br/>
		/// <see cref="ProcessExist"/> can also be used to discover a PID.<br/>
		/// Name: The name of a process is usually the same as its executable (without path), e.g.notepad.exe or winword.exe.<br/>
		/// Since a name might match multiple running processes, only the first process will be operated upon.<br/>
		/// The name is not case-sensitive.
		/// </param>
		/// <returns>The Process ID (PID) of the specified process. If there is no matching process, zero is returned.</returns>
		public static long ProcessExist([UserDeclaredName("PIDOrName")] object pidOrName = null)
		{
			if (!pidOrName.CoerceString(out var name))
				return 0L;

			return string.IsNullOrEmpty(name) ? Environment.ProcessId : FindProcessId(name);
		}

		/// <summary>Returns the parent process ID of the specified process.</summary>
		/// <param name="pidOrName">A process ID or name. If omitted, uses the script's process.</param>
		/// <returns>The parent process ID. Throws a TargetError if the process cannot be found, or an OSError if its parent cannot be retrieved.</returns>
		public static long ProcessGetParent([UserDeclaredName("PIDOrName")] object pidOrName = null)
		{
			if (!pidOrName.CoerceString(out var name))
				return 0L;

			using var proc = string.IsNullOrEmpty(name) ? Process.GetCurrentProcess() : FindProcess(name);

			if (proc == null)
				return (long)Errors.TargetErrorOccurred($"The specified process {pidOrName} was not found", DefaultErrorLong);

			try
			{
				return Platform.Process.GetParentProcessId(proc.Id)
					?? (long)Errors.TargetErrorOccurred($"The specified process {pidOrName} was not found", DefaultErrorLong);
			}
			catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException)
			{
				ThreadAccessors.A_LastError = ex is Win32Exception nativeError ? nativeError.NativeErrorCode : Marshal.GetLastSystemError();
				return (long)Errors.OSErrorOccurred(ex, $"Could not retrieve the parent process of {proc.Id}.", DefaultErrorLong);
			}
		}

		/// <summary>
		/// Returns the executable name of the specified process.
		/// </summary>
		/// <param name="pidOrName">
		/// Specify either a number (the PID) or a process name:<br/>
		/// PID: The Process ID, which is a number that uniquely identifies one specific process (this number is valid only during the lifetime of that process).<br/>
		/// The PID of a newly launched process can be determined via the Run function. Similarly, the PID of a window can be determined with <see cref="WinGetPID"/>. <see cref="ProcessExist"/> can also be used to discover a PID.<br/>
		/// Name: The name of a process is usually the same as its executable (without path), e.g. notepad.exe or winword.exe. Since a name might match multiple running processes, only the first process will be operated upon. The name is not case-sensitive.<br/>
		/// If omitted, the scriptï¿½s own process is used.
		/// </param>
		/// <returns>
		/// The executable name of the specified process, for example: <c>notepad.exe</c>.<br/>
		/// Throws a TargetError if the process could not be found, or an OSError if the name could not be retrieved.
		/// </returns>
		public static string ProcessGetName([UserDeclaredName("PIDOrName")] object pidOrName = null)
			=> ProcessGetPathName(pidOrName, true);

		/// <summary>
		/// Returns the full path of the specified processï¿½s executable.
		/// </summary>
		/// <param name="pidOrName">
		/// Specify either a number (the PID) or a process name:<br/>
		/// PID: The Process ID, which is a number that uniquely identifies one specific process (this number is valid only during the lifetime of that process).<br/>
		/// The PID of a newly launched process can be determined via the Run function. Similarly, the PID of a window can be determined with <see cref="WinGetPID"/>. <see cref="ProcessExist"/> can also be used to discover a PID.<br/>
		/// Name: The name of a process is usually the same as its executable (without path), e.g. notepad.exe or winword.exe. Since a name might match multiple running processes, only the first process will be operated upon. The name is not case-sensitive.<br/>
		/// If omitted, the scriptï¿½s own process is used.
		/// </param>
		/// <returns>
		/// The full path of the specified processï¿½s executable, for example: <c>C:\Windows\notepad.exe</c>.<br/>
		/// Throws a TargetError if the process could not be found, or an OSError if the path could not be retrieved.
		/// </returns>
		public static string ProcessGetPath([UserDeclaredName("PIDOrName")] object pidOrName = null)
			=> ProcessGetPathName(pidOrName, false);

		/// <summary>
		/// Changes the priority level of the first matching process.
		/// </summary>
		/// <param name="level">Specify one of the following words or letters:<br/>
		///     Low(or L)<br/>
		///     BelowNormal(or B)<br/>
		///     Normal(or N)<br/>
		///     AboveNormal(or A)<br/>
		///     High(or H)<br/>
		///     Realtime(or R)<br/>
		/// <param name="pidOrName">
		/// Specify either a number (the PID) or a process name:<br/>
		/// PID: The Process ID, which is a number that uniquely identifies one specific process<br/>
		/// (this number is valid only during the lifetime of that process).<br/>
		/// The PID of a newly launched process can be determined via the Run function.<br/>
		/// Similarly, the PID of a window can be determined with <see cref="WinGetPID"/>.<br/>
		/// <see cref="ProcessExist"/> can also be used to discover a PID.<br/>
		/// Name: The name of a process is usually the same as its executable (without path), e.g.notepad.exe or winword.exe.<br/>
		/// Since a name might match multiple running processes, only the first process will be operated upon.<br/>
		/// The name is not case-sensitive.
		/// </param>
		/// <returns>Returns the Process ID (PID) of the specified process. If a matching process is not found or cannot be manipulated, zero is returned.</returns>
		public static long ProcessSetPriority(object level, [UserDeclaredName("PIDOrName")] object pidOrName = null)
		{
			if (!level.CoerceString(out var lvl) || !pidOrName.CoerceString(out var name))
				return 0L;

			ProcessPriorityClass? priority = lvl.ToLowerInvariant() switch
			{
				"l" or Keyword_Low => ProcessPriorityClass.Idle,
				"b" or Keyword_BelowNormal => ProcessPriorityClass.BelowNormal,
				"n" or Keyword_Normal => ProcessPriorityClass.Normal,
				"a" or Keyword_AboveNormal => ProcessPriorityClass.AboveNormal,
				"h" or Keyword_High => ProcessPriorityClass.High,
				"r" or Keyword_Realtime => ProcessPriorityClass.RealTime,
				_ => null
			};

			if (priority == null)
			{
				_ = Errors.ValueErrorOccurred($"Unknown Level \"{Errors.Describe(level)}\". Expected Low (L), BelowNormal (B), Normal (N), AboveNormal (A), High (H) or Realtime (R).", level);
				return 0;
			}

			using (var proc = string.IsNullOrEmpty(name) ? Process.GetCurrentProcess() : FindProcess(name))
			{
				if (proc != null)
				{
					proc.PriorityClass = priority.Value;

					return proc.Id;
				}
			}

			return 0;
		}

		/// <summary>
		/// Waits for the specified process to exist.
		/// </summary>
		/// <param name="pidOrName">
		/// Specify either a number (the PID) or a process name:<br/>
		/// PID: The Process ID, which is a number that uniquely identifies one specific process<br/>
		/// (this number is valid only during the lifetime of that process).<br/>
		/// The PID of a newly launched process can be determined via the Run function.<br/>
		/// Similarly, the PID of a window can be determined with <see cref="WinGetPID"/>.<br/>
		/// <see cref="ProcessExist"/> can also be used to discover a PID.<br/>
		/// Name: The name of a process is usually the same as its executable (without path), e.g.notepad.exe or winword.exe.<br/>
		/// Since a name might match multiple running processes, only the first process will be operated upon.<br/>
		/// The name is not case-sensitive.
		/// </param>
		/// <param name="timeout">If omitted, the function will wait indefinitely. Otherwise, specify the number of seconds (can contain a decimal point) to wait before timing out.</param>
		/// <returns>The Process ID (PID) of the discovered process. If the function times out, zero is returned.</returns>
		public static long ProcessWait([UserDeclaredName("PIDOrName")] object pidOrName, object timeout = null)
			=> WaitForProcess(pidOrName, timeout, false);

		/// <summary>
		/// Waits for all matching processes to close.
		/// </summary>
		/// <param name="pidOrName">
		/// Specify either a number (the PID) or a process name:<br/>
		/// PID: The Process ID, which is a number that uniquely identifies one specific process<br/>
		/// (this number is valid only during the lifetime of that process).<br/>
		/// The PID of a newly launched process can be determined via the Run function.<br/>
		/// Similarly, the PID of a window can be determined with <see cref="WinGetPID"/>.<br/>
		/// <see cref="ProcessExist"/> can also be used to discover a PID.<br/>
		/// Name: The name of a process is usually the same as its executable (without path), e.g.notepad.exe or winword.exe.<br/>
		/// Since a name might match multiple running processes, only the first process will be operated upon.<br/>
		/// The name is not case-sensitive.
		/// </param>
		/// <param name="timeout">If omitted, the function will wait indefinitely. Otherwise, specify the number of seconds (can contain a decimal point) to wait before timing out.</param>
		/// <returns>0 once no matching process exists, or the PID of a matching process still running when the function times out.</returns>
		public static long ProcessWaitClose([UserDeclaredName("PIDOrName")] object pidOrName, object timeout = null)
			=> WaitForProcess(pidOrName, timeout, true);

		/// <summary>
		/// Runs an external program. Unlike <see cref="Run"/>, <see cref="RunWait"/> will wait until the program finishes before continuing.
		/// </summary>
		/// <param name="target">A document, URL, executable file (.exe, .com, .bat, etc.), shortcut (.lnk), CLSID, or system verb to launch (see remarks).</param>
		/// <param name="workingDir">If blank or omitted, the script's own working directory <see cref="A_WorkingDir"/> will be used.<br/>
		/// Otherwise, specify the initial working directory to be used by the new process.
		/// </param>
		/// <param name="options">If blank or omitted, target will be launched normally. Otherwise, specify one or more of the following options:<br/>
		///     Max: launch maximized<br/>
		///     Min: launch minimized<br/>
		///     Hide: launch hidden(cannot be used in combination with either of the above)
		/// </param>
		/// <param name="outputVarPID">If omitted, the corresponding value will not be stored.<br/>
		/// Otherwise, specify a reference to the output variable in which to store the newly launched program's unique Process ID (PID).
		/// </param>
		/// <param name="args">The arguments to pass to the program.</param>
		/// <returns>Unlike <see cref="Run"/>, <see cref="RunWait"/> will wait until target is closed or exits,<br/>
		/// at which time the return value will be the program's exit code.
		/// </returns>
		public static long Run(object target, object workingDir = null, object options = null, [ByRef] object outputVarPID = null, object args = null)
		{
			if (!target.CoerceString(out var targetText) || !workingDir.CoerceString(out var dir) || !options.CoerceString(out var opts) || !args.CoerceString(out var argsText))
				return 0L;

			return RunInternal(targetText, dir, opts, outputVarPID, argsText);
		}

		/// <summary>
		/// Specifies a set of user credentials to use for all subsequent uses of <see cref="Run"/>.
		/// Leave all parameters blank to use no credentials.
		/// </summary>
		/// <param name="user">If this and the other parameters are all omitted, the RunAs feature will be turned off,<br/>
		/// which restores <see cref="Run"/> and <see cref="RunWait"/> to their default behavior.<br/>
		/// Otherwise, specify the username under which new processes will be created.
		/// </param>
		/// <param name="password">If blank or omitted, it defaults to a blank password. Otherwise, specify the User's password.</param>
		/// <param name="domain">If blank or omitted, a local account will be used. Otherwise, specify User's domain. If that fails to work, try using @YourComputerName.</param>
		public static object RunAs(object user = null, object password = null, object domain = null)
		{
			if (!user.CoerceString(out var u) || !password.CoerceString(out var p) || !domain.CoerceString(out var d))
				return DefaultObject;

			var script = Script.TheScript;
			script.ProcessesData.runUser = u;
			script.ProcessesData.runDomain = d;

			if (string.IsNullOrEmpty(p))
			{
				script.ProcessesData.runPassword = null;
			}
			else
			{
				script.ProcessesData.runPassword = new SecureString();

				foreach (var sym in p)
					script.ProcessesData.runPassword.AppendChar(sym);

				script.ProcessesData.runPassword.MakeReadOnly();
			}

			return DefaultObject;
		}


		/// <summary>
		/// Runs an external program.<br/>
		/// Unlike Run, <see cref="RunWait"/> will wait until the program finishes before continuing.
		/// <see cref="Run"/>.
		/// </summary>
		public static long RunWait(object target, object workingDir = null, object options = null, [ByRef] object outputVarPID = null, object args = null)
		{
			if (!target.CoerceString(out var targetText) || !workingDir.CoerceString(out var dir) || !options.CoerceString(out var opts) || !args.CoerceString(out var argsText))
				return 0L;

			return RunInternal(targetText, dir, opts, outputVarPID, argsText, true);
		}

		/// <summary>
		/// Shuts down, restarts, or logs off the system.
		/// </summary>
		/// <param name="flags">A combination (sum) of the following numbers:<br/>
		/// 0: Logoff<br/>
		/// 1: Shutdown<br/>
		/// 2: Reboot<br/>
		/// 4: Force<br/>
		/// 8: Power down<br/>
		/// Add the required values together.<br/>
		/// For example, to shutdown and power down the flag would be 9 (shutdown + power down = 1 + 8 = 9).<br/>
		/// The "Force" value (4) forces all open applications to close.<br/>
		/// It should only be used in an emergency because it may cause any open applications to lose data.<br/>
		/// The "Power down" value (8) shuts down the system and turns off the power.
		/// </param>
		public static object Shutdown(object flags)
		{
			if (!flags.CoerceInt(out var f))
				return DefaultObject;

			_ = Platform.Session.ExitProgram(unchecked((uint)f), 0);
			return DefaultObject;
		}

		/// <summary>
		/// Internal helper to find a process by name or ID.
		/// </summary>
		/// <param name="name">The name or ID of the process to find.</param>
		/// <returns>The <see cref="Process"/> object if found, else null.</returns>
		private static Process FindProcess(string name)
		{
			if (int.TryParse(name, out var id))
			{
				try
				{
					return Process.GetProcessById(id);
				}
				catch
				{
					//Keep searching, the process might have a name that is all digits with no extension.
				}
			}

			return FindProcessByName(name);
		}

		private static Process FindProcessByName(string name)
		{
			const string exe = ".exe";

			if (name.EndsWith(exe, StringComparison.OrdinalIgnoreCase))
				name = name.Substring(0, name.Length - exe.Length);

			try
			{
				var prc = Process.GetProcessesByName(name);
				var found = prc.Length > 0 ? prc[0] : null;

				for (var i = found == null ? 0 : 1; i < prc.Length; i++)
					prc[i].Dispose();

				return found;
			}
			catch
			{
				return null;
			}
		}

		/// <summary>The PID of the first process <see cref="FindProcess"/> matches, or 0 when none does.</summary>
		private static long FindProcessId(string name)
		{
			if (int.TryParse(name, out var id) && Platform.Process.Exists(id))
				return id;

			using var proc = FindProcessByName(name);
			return proc?.Id ?? 0L;
		}

		/// <summary>
		/// <see cref="ProcessGetName"/> and <see cref="ProcessGetPath"/>, as AutoHotkey's ProcessGetPathName serves both:
		/// the executable's file name with <paramref name="nameOnly"/>, otherwise its full path.
		/// </summary>
		private static string ProcessGetPathName(object pidOrName, bool nameOnly)
		{
			if (!pidOrName.CoerceString(out var name))
				return "";

			using var proc = string.IsNullOrEmpty(name) ? Process.GetCurrentProcess() : FindProcess(name);

			if (proc == null)
				return (string)Errors.TargetErrorOccurred($"The specified process {pidOrName} was not found");

#if WINDOWS
			var result = GetProcessImage((uint)proc.Id, nameOnly);
			return result.Length != 0 ? result : (string)Errors.OSErrorOccurred(new Win32Exception(Marshal.GetLastWin32Error()), "", DefaultErrorString);
#else
			using var module = proc.MainModule;
			return nameOnly ? module.ModuleName : module.FileName;
#endif
		}

		/// <summary>
		/// <see cref="ProcessWait"/> and, with <paramref name="waitClose"/>, <see cref="ProcessWaitClose"/>, as
		/// AutoHotkey's ProcessWait serves both. A name can match several processes, so a close lasts until none
		/// remains. An omitted timeout waits indefinitely and a negative one checks once.
		/// </summary>
		/// <returns>The PID found last: a match for ProcessWait, and for ProcessWaitClose 0 or the match still running.</returns>
		private static long WaitForProcess(object pidOrName, object timeout, bool waitClose)
		{
			if (!pidOrName.CoerceString(out var name) || !timeout.CoerceDouble(out var seconds))
				return 0L;

			var timeoutMs = timeout == null ? -1 : (int)Math.Clamp(seconds * 1000, 0, int.MaxValue);
			var start = Environment.TickCount64;

			while (true)
			{
				using var process = FindProcess(name);
				var pid = process?.Id ?? 0L;
				var remaining = timeoutMs < 0 ? -1 : (int)Math.Max(0, timeoutMs - (Environment.TickCount64 - start));

				if ((waitClose ? pid == 0 : pid != 0) || remaining == 0)
					return pid;

				if (waitClose)
				{
					try
					{
						var exited = process.WaitForExitAsync();

						if (!Keysharp.Internals.Flow.WaitForCompletion(exited, remaining))
							return pid;

						exited.GetAwaiter().GetResult();
						continue;
					}
					catch (Win32Exception)
					{
						// Process discovery may succeed even when synchronization fails.
					}
				}

				Keysharp.Internals.Flow.Sleep(remaining < 0 ? ProcessPollInterval : Math.Min(remaining, ProcessPollInterval));
			}
		}

#if WINDOWS
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
				const int capacity = 1024;
				var buf = stackalloc char[capacity];

				// GetProcessImageFileName is the faster call, but it gives a device path, so only the name can use it.
				if (nameOnly)
				{
					var path = new ReadOnlySpan<char>(buf, (int)WindowsAPI.GetProcessImageFileName(hProc, buf, capacity));
					return path[(path.LastIndexOf('\\') + 1)..].ToString();
				}

				var size = (uint)capacity;
				return WindowsAPI.QueryFullProcessImageName(hProc, 0, buf, ref size) ? new string(buf, 0, (int)size) : "";
			}
			finally
			{
				_ = WindowsAPI.CloseHandle(hProc);
			}
		}
#endif

#if !WINDOWS
		private static bool IsUrlTarget(string target)
		{
			if (string.IsNullOrEmpty(target))
				return false;

			if (!Uri.TryCreate(target, UriKind.Absolute, out var uri))
				return false;

			var scheme = uri.Scheme;
			return scheme == Uri.UriSchemeHttp
				|| scheme == Uri.UriSchemeHttps
				|| scheme == Uri.UriSchemeFtp
				|| scheme == Uri.UriSchemeMailto
				|| scheme == Uri.UriSchemeFile;
		}
#endif

		private static bool RunAsSpecified()
		{
			var script = Script.TheScript;
			return (script.ProcessesData.runPassword != null && script.ProcessesData.runPassword.Length > 0)
				   || (!string.IsNullOrEmpty(script.ProcessesData.runUser))
				   || (!string.IsNullOrEmpty(script.ProcessesData.runDomain));
		}

		/// <summary>
		/// Internal helper to run a process. <see cref="Run"/>, <see cref="RunAs"/>, <see cref="RunWait"/>
		/// </summary>
		/// <exception cref="Error">An <see cref="Error"/> exception is thrown on failure.</exception>
		private static long RunInternal(string target, string workingDir, string showMode, [ByRef] object outputVarPID, string args, bool wait = false)
		{
			ThreadAccessors.A_LastError = 0;
			var pid = 0L;
			var useRunAs = RunAsSpecified();

			if (string.IsNullOrEmpty(target))//AHK returns 1 as a success for an empty run target.
			{
				if (outputVarPID != null) Refs.SetValue(outputVarPID, "");
				return 1L;
			}

			if (!string.IsNullOrEmpty(workingDir))
			{
				workingDir = workingDir.Trim();

				if (!Directory.Exists(workingDir))
					return (long)Errors.ErrorOccurred($"{workingDir} is not a valid directory.", DefaultErrorLong);
			}

			using var prc = new Process
			{
				StartInfo = new ProcessStartInfo
				{
					WorkingDirectory = workingDir,
					UseShellExecute = true
				}
			};

			try
			{
				var script = Script.TheScript;
				string shellVerb = null, shellAction = target;
				args = args.Trim();

				if (!string.IsNullOrEmpty(args))//Args were passed separately.
				{
					if (shellAction.StartsWith('*') || verbs.Contains(shellAction))
					{
						shellVerb = shellAction.TrimStart('*');
						shellAction = args;
						args = "";
					}
				}
				else//Try to parse args out of target.
				{
					var firstSpace = shellAction.IndexOfAny(SpaceTab);

					if (firstSpace > 0)
					{
						var phrase = shellAction.Substring(0, firstSpace);

						if (phrase[0] == '*')
							shellVerb = phrase.Substring(1);
						else if (verbs.Contains(phrase))
							shellVerb = phrase;

						if (!string.IsNullOrEmpty(shellVerb))
							shellAction = shellAction.AsSpan(firstSpace + 1).TrimStart().ToString();
					}
				}

				var hasVerb = !string.IsNullOrEmpty(shellVerb);

				if (useRunAs && hasVerb)
					return (long)Errors.ErrorOccurred("System verbs unsupported with RunAs.", DefaultErrorLong);

				var parsedArgs = "";
				var executable = hasVerb ? shellAction : target;

				if (executable.StartsWith('"'))
				{
					// Quotes delimit the executable path; they are not part of ProcessStartInfo.FileName.
					var nextQuote = executable.IndexOf('"', 1);

					if (nextQuote > 0)
					{
						parsedArgs = executable.AsSpan(nextQuote + 1).Trim().ToString();
						executable = executable.AsSpan(1, nextQuote - 1).Trim().ToString();
					}
					else
						executable = executable.Substring(1);
				}
				else
				{
					var nextSpace = executable.IndexOfAny(SpaceTab, 1);

					if (nextSpace > 0)
					{
						var program = executable.AsSpan(0, nextSpace).Trim().ToString();

						// Split PATH-resolved commands, but keep an existing unquoted path containing spaces intact.
						if (System.IO.File.Exists(System.IO.Path.Combine(workingDir, program))
							|| !System.IO.Path.Exists(System.IO.Path.Combine(workingDir, executable)))
						{
							parsedArgs = executable.AsSpan(nextSpace + 1).Trim().ToString();
							executable = program;
						}
					}
				}

				prc.StartInfo.FileName = executable;

				if (hasVerb)
					prc.StartInfo.Verb = shellVerb;
				else
				{
					prc.StartInfo.UserName = string.IsNullOrEmpty(script.ProcessesData.runUser) ? null : script.ProcessesData.runUser;
#if WINDOWS
					prc.StartInfo.Domain = string.IsNullOrEmpty(script.ProcessesData.runDomain) ? null : script.ProcessesData.runDomain;
					prc.StartInfo.Password = (script.ProcessesData.runPassword == null || script.ProcessesData.runPassword.Length == 0) ? null : script.ProcessesData.runPassword;
#endif
				}

				prc.StartInfo.Arguments = !string.IsNullOrEmpty(args) ? args : parsedArgs;

#if !WINDOWS
				if (hasVerb)
					return (long)Errors.TargetErrorOccurred($"Run verbs ('*{shellVerb}') are not supported on this platform.", DefaultErrorLong);

				// On Linux/macOS, UseShellExecute=true delegates to xdg-open/open, which does not
				// forward arguments. Use it only when the target is an URL; otherwise run the
				// executable directly so Arguments are passed verbatim.
				prc.StartInfo.UseShellExecute = IsUrlTarget(prc.StartInfo.FileName);
#endif

				if (!string.IsNullOrEmpty(showMode))
				{
					switch (showMode)
					{
						case var x when x.Equals(Keyword_Max, StringComparison.OrdinalIgnoreCase): prc.StartInfo.WindowStyle = ProcessWindowStyle.Maximized; break;

						case var x when x.Equals(Keyword_Min, StringComparison.OrdinalIgnoreCase): prc.StartInfo.WindowStyle = ProcessWindowStyle.Minimized; break;

						case var x when x.Equals(Keyword_Hide, StringComparison.OrdinalIgnoreCase): prc.StartInfo.WindowStyle = ProcessWindowStyle.Hidden; break;
					}
				}

				if (prc.Start())
					pid = prc.Id;
			}
			catch (Exception ex)
			{
				ThreadAccessors.A_LastError = Marshal.GetLastSystemError();
				return (long)Errors.ErrorOccurred(ex.Message, DefaultErrorLong);
			}

			// Assigned before RunWait waits, as AutoHotkey does, so a thread that runs meanwhile can use it.
			if (outputVarPID != null) Refs.SetValue(outputVarPID, pid != 0 ? pid : "");

			// No process to wait for, such as a document handed to an application that was already running.
			if (!wait || pid == 0)
				return 0L;

			// Pumped as AutoHotkey's MsgWaitForMultipleObjects wait is, so timers, hotkeys and the GUI keep running, and woken
			// by the exit itself.
			_ = Keysharp.Internals.Flow.WaitForCompletion(prc.WaitForExitAsync(), -1);
			return prc.ExitCode;
		}
	}

	/// <summary>
	/// Encapsulates information and I/O for a spawned <see cref="Process"/>.
	/// </summary>
	public class ScriptProcess : KeysharpObject, IDisposable
	{
		private readonly Process process;
		private string assemblyTransportPath;
		private object exitCallback;
		private ScriptEventScheduler callbackScheduler;
		private ScriptEventScheduler.PendingCallbackRegistration callbackRegistration;
		private Task<string> capturedStdOut;
		private Task<string> capturedStdErr;
		private bool disposed;

		public ScriptProcess(params object[] args) : base(args)
		{
			process = args[0] as Process;
			WatchForExit();
		}

		internal ScriptProcess(Process process, string transportPath) : base(null)
		{
			this.process = process;
			assemblyTransportPath = transportPath;
			WatchForExit();
		}

		public long HasExited => process.HasExited ? 1L : 0L;
		public object ExitCode => process.HasExited ? (long)process.ExitCode : "";
		/// <summary>
		/// Gets the exit time formatted as "YYYYMMDDHH24MISS", or an empty string while the process is running.
		/// </summary>
		public string ExitTime => process.HasExited ? Conversions.ToYYYYMMDDHH24MISS(process.ExitTime) : "";
		/// <summary>
		/// Returns a KeysharpFile wrapping the standard output stream.
		/// </summary>
		private object stdOut;
		public object StdOut => GetOutput(false);
		/// <summary>
		/// Returns a KeysharpFile wrapping the standard error stream.
		/// </summary>
		private object stdErr;
		public object StdErr => GetOutput(true);
		/// <summary>
		/// Returns a KeysharpFile wrapping the standard input stream.
		/// </summary>
		private object stdIn;
		public object StdIn => stdIn ??= new KeysharpFile(process.StandardInput);
		/// <summary>
		/// Immediately kills the underlying process.
		/// </summary>
		/// <returns></returns>
		public object Kill()
		{
			process.Kill();
			return DefaultObject;
		}

		public object Close()
		{
			Dispose(true);
			return DefaultObject;
		}

		internal bool AttachExitCallback(object callback, ScriptEventScheduler scheduler)
		{
			if (callback == null)
				return true;

			exitCallback = callback;
			callbackScheduler = scheduler;

			if ((callbackRegistration = scheduler?.RegisterPendingCallback(CancelCallback)) != null)
				return true;

			CancelCallback();
			return false;
		}

		/// <summary>
		/// Waits for the process and its output, pumping meanwhile as RunWait does. The output is kept as the reads
		/// themselves, since the exit callback can run during the wait, before they have finished.
		/// </summary>
		internal void CaptureAndWait()
		{
			capturedStdOut = process.StandardOutput.ReadToEndAsync();
			capturedStdErr = process.StandardError.ReadToEndAsync();
			_ = Keysharp.Internals.Flow.WaitForCompletion(Task.WhenAll(process.WaitForExitAsync(), capturedStdOut, capturedStdErr), -1);
		}

		internal void StartFailed()
		{
			CleanupTransport();
			_ = Close();
		}

		private void WatchForExit()
		{
			if (process == null)
				return;

			process.EnableRaisingEvents = true;
			process.Exited += ProcessExited;
		}

		private void ProcessExited(object sender, EventArgs e)
		{
			CleanupTransport();
			process.Exited -= ProcessExited;
			DispatchCallback();
		}

		private object GetOutput(bool error)
		{
			ref var result = ref error ? ref stdErr : ref stdOut;

			if (result != null)
				return result;

			var captured = error ? capturedStdErr : capturedStdOut;
			return result = captured != null
				? new KeysharpFile(new StringReader(captured.GetAwaiter().GetResult()))
				: new KeysharpFile(error ? process.StandardError : process.StandardOutput);
		}

		private void DispatchCallback()
		{
			var scheduler = Volatile.Read(ref callbackScheduler);

			if (scheduler == null || !scheduler.Enqueue(ScriptEventQueue.Normal, 0, RunCallback))
				CancelCallback();
		}

		private ScriptEventExecutionResult RunCallback()
		{
			var scheduler = Volatile.Read(ref callbackScheduler);
			var callback = Volatile.Read(ref exitCallback);

			if (scheduler == null || callback == null)
				return ScriptEventExecutionResult.Dropped;

			var result = scheduler.TryExecuteThreadLaunch(0, false, false, threadVariables =>
			{
				try
				{
					_ = Keysharp.Internals.Flow.TryCatch(() => _ = Script.InvokeOrNull(callback, null, this));
				}
				finally
				{
					CancelCallback();
				}
			}, ThreadKind.Callback);

			if (result == ScriptEventExecutionResult.Dropped)
				CancelCallback();

			return result;
		}

		private void CancelCallback()
		{
			_ = Interlocked.Exchange(ref callbackScheduler, null);
			_ = Interlocked.Exchange(ref exitCallback, null);
			Interlocked.Exchange(ref callbackRegistration, null)?.Dispose();
		}

		private void CleanupTransport()
		{
			var path = Interlocked.Exchange(ref assemblyTransportPath, null);

			if (path == null)
				return;

			try { File.Delete(path); }
			catch (IOException) { }
			catch (UnauthorizedAccessException) { }
		}

		protected virtual void Dispose(bool disposing)
		{
			if (disposed)
				return;

			disposed = true;
			CancelCallback();

			if (disposing)
			{
				process.Exited -= ProcessExited;
				(stdIn as IDisposable)?.Dispose();
				(stdOut as IDisposable)?.Dispose();
				(stdErr as IDisposable)?.Dispose();
				process?.Dispose();
			}
		}

		// Collection and exit only stop the exit callback: the streams it handed out may still be in use.
		void IDisposable.Dispose()
		{
			Dispose(false);
			HasFinalizer = false;
		}
	}
}
