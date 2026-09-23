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
		private const int LoopFrequency = 50;

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
			var name = pidOrName.As(); //Will handle name string or pid int.

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
			var name = pidOrName.As();

			using (var proc = string.IsNullOrEmpty(name) ? Process.GetCurrentProcess() : FindProcess(name))
			{
				return proc != null ? proc.Id : 0L;
			}
		}

		/// <summary>Returns the parent process ID of the specified process.</summary>
		/// <param name="pidOrName">A process ID or name. If omitted, uses the script's process.</param>
		/// <returns>The parent process ID. Throws a TargetError if the process cannot be found, or an OSError if its parent cannot be retrieved.</returns>
		public static long ProcessGetParent([UserDeclaredName("PIDOrName")] object pidOrName = null)
		{
			var name = pidOrName.As();

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
		{
			var name = pidOrName.As();

			using (var proc = string.IsNullOrEmpty(name) ? Process.GetCurrentProcess() : FindProcess(name))
			{
				if (proc == null)
					return (string)Errors.TargetErrorOccurred($"The specified process {pidOrName} was not found");

#if WINDOWS

				if (GetProcessName((uint)proc.Id, out string result) == 0)
					return (string)Errors.OSErrorOccurred(new Win32Exception(Marshal.GetLastWin32Error()), "", DefaultErrorString);

				return result;
#else
				using var module = proc.MainModule;
				return module.ModuleName;
#endif
			}
		}

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
		{
			var name = pidOrName.As();

			using (var proc = string.IsNullOrEmpty(name) ? Process.GetCurrentProcess() : FindProcess(name))
			{
#if WINDOWS

				if (GetProcessName((uint)proc.Id, out string result, false) == 0)
					return (string)Errors.OSErrorOccurred(new Win32Exception(Marshal.GetLastWin32Error()), "", DefaultErrorString);

				return result;
#else
				using var module = proc.MainModule;
				return module.FileName;
#endif
			}
		}

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
			var lvl = level.As();
			var name = pidOrName.As();
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
		{
			var name = pidOrName.As();
			var time = timeout.Ad(-1.0);
			var t = time;
			Process proc;

			if (t >= 0)
				t = time * 1000;

			var start = DateTime.UtcNow;

			while ((proc = FindProcess(name)) == null)
			{
				_ = Flow.Sleep(LoopFrequency);

				if (t >= 0.0 && (DateTime.UtcNow - start).TotalMilliseconds > t)
					break;
			}

			long id = 0L;

			if (proc != null)
			{
				id = proc.Id;
				proc.Dispose();
			}

			return id;
		}

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
		/// <returns></returns>
		public static long ProcessWaitClose([UserDeclaredName("PIDOrName")] object pidOrName, object timeout = null)
		{
			var name = pidOrName.As();
			var time = timeout.Ad(-1.0);

			using (var proc = FindProcess(name))
			{
				if (proc != null)
				{
					var pid = proc.Id;

					if (time >= 0)
						_ = proc.WaitForExit((int)(time * 1000));
					else
						proc.WaitForExit();

					return pid;
				}
			}

			return 0L;
		}

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
			return RunInternal(target.As(), workingDir.As(), options.As(), outputVarPID, args.As());
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
			var u = user.As();
			var p = password.As();
			var d = domain.As();
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
			return RunInternal(target.As(), workingDir.As(), options.As(), outputVarPID, args.As(), true);
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
			_ = Platform.Session.ExitProgram((uint)flags.Al(), 0);
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

#if WINDOWS
		/// <summary>
		/// Internal helper to get a process name by PID.
		/// </summary>
		/// <param name="pid">The PID of the process.</param>
		/// <param name="result">Set to the name of the process, or error string.</param>
		/// <param name="getNameOnly">When true then only the name is returned (eg "notepad.exe"),
		/// otherwise the full path.</param>
		/// <returns>The length of the returned string.</returns>
		internal static uint GetProcessName(uint pid, out string result, bool getNameOnly = true)
		{
			const int MAX_PATH = 1024;
			result = DefaultErrorString;
			var buf = new char[MAX_PATH];
			nint hProc = WindowsAPI.OpenProcess(ProcessAccessTypes.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);

			if (hProc == 0)
				return 0;

			try
			{
				uint len = WindowsAPI.GetProcessImageFileName(hProc, buf, (uint)buf.Length);

				if (len == 0)
					return 0;

				string path = new string(buf, 0, (int)len);

				if (getNameOnly)
				{
					int idx = path.LastIndexOf('\\');
					result = (idx >= 0) ? path.Substring(idx + 1) : path;
					return (uint)result.Length;
				}

				// convert device path (\Device\HarddiskVolumeX\...) to drive letter C:\ï¿½
				var device = new char[MAX_PATH];
				var logicalPath = path;

				for (char drv = 'A'; drv <= 'Z'; drv++)
				{
					string drive = drv + ":";
					uint rc = WindowsAPI.QueryDosDevice(drive, device, (uint)device.Length);

					if (rc == 0)
						continue;

					string devPath = new string(device, 0, (int)rc);

					if (path.StartsWith(devPath + "\\", StringComparison.OrdinalIgnoreCase))
					{
						logicalPath = drive + path.Substring(devPath.Length);
						break;
					}
				}

				result = logicalPath;
				return (uint)result.Length;
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
			var pid = 0;
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

			try
			{
				var script = Script.TheScript;
				string shellVerb = null, shellAction = target, shellParams = null;
				args = args.Trim();

				if (!string.IsNullOrEmpty(args))//Args were passed separately.
				{
					if (shellAction.StartsWith('*'))
					{
						shellAction = shellAction.TrimStart('*');
						shellVerb = shellAction;
						shellAction = args;
					}
					else if (verbs.Contains(target))
					{
						shellVerb = shellAction;
						shellAction = args;
					}
					else
						shellParams = args;
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
							shellAction = shellAction.Substring(firstSpace + 1);
					}
				}

				if (useRunAs && !string.IsNullOrEmpty(shellVerb))
					return (long)Errors.ErrorOccurred("System verbs unsupported with RunAs.", DefaultErrorLong);

				var parsedArgs = "";
				var prc = new Process
				{
					StartInfo = new ProcessStartInfo
					{
						WorkingDirectory = workingDir,
						UseShellExecute = true
					}
				};
				//MessageBox.Show(Accessors.A_WorkingDir.ToString());

				if (string.IsNullOrEmpty(shellVerb))
				{
					if (target.StartsWith('"'))
					{
						//Quoting the program is how a path with spaces can still carry arguments. The quotes
						//delimit the name within the command line and are not part of it, so they come off
						//here: FileName is a path. ShellExecute tolerates them, exec looks the name up verbatim.
						var nextQuote = target.IndexOf('"', 1);

						if (nextQuote > 0)
						{
							parsedArgs = target.Substring(nextQuote + 1).Trim();
							target = target.Substring(1, nextQuote - 1).Trim();
						}
						else
							target = target.Substring(1);//The closing quote was missing, which is very unlikely.
					}
					else
					{
						var nextSpace = target.IndexOfAny(SpaceTab, 1);

						if (nextSpace > 0)
						{
							object oldDir = "";
							var temp = target.Substring(0, nextSpace).Trim();
							var setWorkingDir = !string.IsNullOrEmpty(workingDir) && System.IO.Path.Exists(workingDir);

							if (setWorkingDir)
							{
								oldDir = A_WorkingDir;
								A_WorkingDir = workingDir;
							}

							//Per AHK, parameters follow the program/document name, so split at the first space.
							//Skip the split only when the whole unquoted target is itself an existing path that
							//contains spaces (such paths should be quoted, but support them anyway); otherwise
							//split so PATH-resolved commands like "xclock -foo" pass their args correctly.
							if (System.IO.Path.Exists(temp) || !System.IO.Path.Exists(target))
							{
								parsedArgs = target.Substring(nextSpace + 1).Trim();
								target = temp;
							}

							if (setWorkingDir)
								A_WorkingDir = oldDir;
						}
					}

					prc.StartInfo.FileName = target;
					prc.StartInfo.UserName = string.IsNullOrEmpty(script.ProcessesData.runUser) ? null : script.ProcessesData.runUser;
#if WINDOWS
					prc.StartInfo.Domain = string.IsNullOrEmpty(script.ProcessesData.runDomain) ? null : script.ProcessesData.runDomain;
					prc.StartInfo.Password = (script.ProcessesData.runPassword == null || script.ProcessesData.runPassword.Length == 0) ? null : script.ProcessesData.runPassword;
#endif
				}
				else
				{
					if (string.IsNullOrEmpty(shellParams))//Attempt to parse out args.
					{
						if (shellAction.StartsWith('"'))
						{
							//See the matching note above: the quotes delimit the name and are not part of it.
							var nextQuote = shellAction.IndexOf('"', 1);

							if (nextQuote > 0)
							{
								shellParams = shellAction.Substring(nextQuote + 1).Trim();
								shellAction = shellAction.Substring(1, nextQuote - 1).Trim();
								parsedArgs = shellParams;
							}
							else
								shellAction = shellAction.Substring(1);//The closing quote was missing, which is very unlikely.
						}
						else
						{
							var nextSpace = shellAction.IndexOfAny(SpaceTab, 1);

							if (nextSpace > 0)
							{
								object oldDir = "";
								var temp = shellAction.Substring(0, nextSpace).Trim();
								var setWorkingDir = !string.IsNullOrEmpty(workingDir) && System.IO.Path.Exists(workingDir);

								if (setWorkingDir)
								{
									oldDir = A_WorkingDir;
									A_WorkingDir = workingDir;
								}

								//See the matching note above: split program from params at the first space unless the
								//whole unquoted action is itself an existing path containing spaces.
								if (System.IO.Path.Exists(temp) || !System.IO.Path.Exists(shellAction))
								{
									shellParams = shellAction.Substring(nextSpace + 1).Trim();
									shellAction = temp;
									parsedArgs = shellParams;
								}

								if (setWorkingDir)
									A_WorkingDir = oldDir;
							}
						}
					}

					prc.StartInfo.FileName = shellAction;
					prc.StartInfo.Verb = shellVerb;
				}

				prc.StartInfo.Arguments = !string.IsNullOrEmpty(args) ? args : parsedArgs;

#if !WINDOWS
				if (!string.IsNullOrEmpty(shellVerb))
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
				{
					pid = prc.Id;

					if (wait)
					{
                        prc.WaitForExit();
						if (outputVarPID != null ) Refs.SetValue(outputVarPID, pid);
						return prc.ExitCode;
					}
				}
			}
			catch (Exception ex)
			{
				ThreadAccessors.A_LastError = Marshal.GetLastSystemError();
				return (long)Errors.ErrorOccurred(ex.Message, DefaultErrorLong);
			}

			if (outputVarPID != null) Refs.SetValue(outputVarPID, pid != 0 ? pid : "");
            return 0L;
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
		private Action callbackInvalidated;
		private string capturedStdOut;
		private string capturedStdErr;
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

		~ScriptProcess() => Dispose(false);

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
			GC.SuppressFinalize(this);
			HasFinalizer = false;
			return DefaultObject;
		}

		internal bool AttachExitCallback(object callback, ScriptEventScheduler scheduler)
		{
			if (callback == null)
				return true;

			exitCallback = callback;
			callbackScheduler = scheduler;
			callbackInvalidated = InvalidateCallback;

			if (scheduler != null && scheduler.RegisterPendingCallback(callbackInvalidated))
				return true;

			InvalidateCallback();
			return false;
		}

		internal void CaptureAndWait()
		{
			var stdout = process.StandardOutput.ReadToEndAsync();
			var stderr = process.StandardError.ReadToEndAsync();
			process.WaitForExit();
			capturedStdOut = stdout.GetAwaiter().GetResult();
			capturedStdErr = stderr.GetAwaiter().GetResult();
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
				? new KeysharpFile(new StringReader(captured))
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

		private void InvalidateCallback()
		{
			_ = Interlocked.Exchange(ref exitCallback, null);
			_ = Interlocked.Exchange(ref callbackScheduler, null);
			_ = Interlocked.Exchange(ref callbackInvalidated, null);
		}

		private void CancelCallback()
		{
			var scheduler = Interlocked.Exchange(ref callbackScheduler, null);
			var invalidated = Interlocked.Exchange(ref callbackInvalidated, null);
			_ = Interlocked.Exchange(ref exitCallback, null);
			scheduler?.ReleasePendingCallback(invalidated);
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

		void IDisposable.Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
			HasFinalizer = false;
		}
	}
}
