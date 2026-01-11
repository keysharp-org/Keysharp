namespace Keysharp.Core
{
	internal class ProcessesData
	{
		internal SynchronizationContext mainContext;

		//internal int CurrentThreadID = Process.GetCurrentProcess().Threads[0].Id; //WindowsAPI.GetCurrentThread();
		internal uint CurrentThreadID = 0u;
		internal uint MainThreadID;
		internal int ManagedMainThreadID;
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
		public static long ProcessClose(object pidOrName)
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
				catch (Win32Exception) { }
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
		public static long ProcessExist(object pidOrName = null)
		{
			var name = pidOrName.As();

			using (var proc = string.IsNullOrEmpty(name) ? Process.GetCurrentProcess() : FindProcess(name))
			{
				return proc != null ? proc.Id : 0L;
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
		/// If omitted, the script�s own process is used.
		/// </param>
		/// <returns>
		/// The executable name of the specified process, for example: <c>notepad.exe</c>.<br/>
		/// Throws a TargetError if the process could not be found, or an OSError if the name could not be retrieved.
		/// </returns>
		public static string ProcessGetName(object pidOrName = null)
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
		/// Returns the full path of the specified process�s executable.
		/// </summary>
		/// <param name="pidOrName">
		/// Specify either a number (the PID) or a process name:<br/>
		/// PID: The Process ID, which is a number that uniquely identifies one specific process (this number is valid only during the lifetime of that process).<br/>
		/// The PID of a newly launched process can be determined via the Run function. Similarly, the PID of a window can be determined with <see cref="WinGetPID"/>. <see cref="ProcessExist"/> can also be used to discover a PID.<br/>
		/// Name: The name of a process is usually the same as its executable (without path), e.g. notepad.exe or winword.exe. Since a name might match multiple running processes, only the first process will be operated upon. The name is not case-sensitive.<br/>
		/// If omitted, the script�s own process is used.
		/// </param>
		/// <returns>
		/// The full path of the specified process�s executable, for example: <c>C:\Windows\notepad.exe</c>.<br/>
		/// Throws a TargetError if the process could not be found, or an OSError if the path could not be retrieved.
		/// </returns>
		public static string ProcessGetPath(object pidOrName = null)
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
		public static long ProcessSetPriority(object level, object pidOrName = null)
		{
			var lvl = level.As();
			var name = pidOrName.As();
			var arg = lvl.ToLowerInvariant();

			using (var proc = string.IsNullOrEmpty(name) ? Process.GetCurrentProcess() : FindProcess(name))
			{
				if (proc != null)
				{
					if (arg.Length == 1)
					{
						foreach (var mode in new[] { Keyword_Low, Keyword_BelowNormal, Keyword_Normal, Keyword_AboveNormal, Keyword_High, Keyword_Realtime })
						{
							if (mode[0] == arg[0])
							{
								arg = mode;
								break;
							}
						}
					}

					switch (arg)
					{
						case Keyword_Low: proc.PriorityClass = ProcessPriorityClass.Idle; break;

						case Keyword_BelowNormal: proc.PriorityClass = ProcessPriorityClass.BelowNormal; break;

						case Keyword_Normal: proc.PriorityClass = ProcessPriorityClass.Normal; break;

						case Keyword_AboveNormal: proc.PriorityClass = ProcessPriorityClass.AboveNormal; break;

						case Keyword_High: proc.PriorityClass = ProcessPriorityClass.High; break;

						case Keyword_Realtime: proc.PriorityClass = ProcessPriorityClass.RealTime; break;
					}

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
		public static long ProcessWait(object pidOrName, object timeout = null)
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
		public static long ProcessWaitClose(object pidOrName, object timeout = null)
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
		/// <see cref="Run(object, object, object, ref object, object)"/>
		/// </summary>
		public static long Run(object target, object workingDir = null, object options = null)
		{
			object pid = VarRef.Empty;
			return Run(target, workingDir, options, pid, null);
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
		public static long Run(object target, object workingDir, object options, object outputVarPID, object args = null)
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
		/// <see cref="RunWait(object, object, object, ref object, object)"/>.
		/// </summary>
		public static long RunWait(object target, object workingDir = null, object options = null)
		{
			return RunWait(target, workingDir, options, null, null);
		}

		/// <summary>
		/// Runs an external program.<br/>
		/// Unlike Run, <see cref="RunWait"/> will wait until the program finishes before continuing.
		/// <see cref="Run"/>.
		/// </summary>
		public static long RunWait(object target, object workingDir, object options, [ByRef] object outputVarPID, object args = null)
		{
			return RunInternal(target.As(), workingDir.As(), options.As(), outputVarPID, args.As(), true);
		}

		/// <summary>
		/// Shuts down, restarts, or logs off the system.
		/// <param name="code">A combination (sum) of the following numbers:<br/>
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
		/// </summary>
		public static object Shutdown(object obj)
		{
			_ = ExitProgram((uint)obj.Al(), 0);
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
				return prc.Length > 0 ? prc[0] : null;
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

				// convert device path (\Device\HarddiskVolumeX\...) to drive letter C:\�
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
			outputVarPID ??= VarRef.Empty;
			var pid = 0;
			var useRunAs = RunAsSpecified();

			if (string.IsNullOrEmpty(target))//AHK returns 1 as a success for an empty run target.
				return 1L;

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
				var prc = new Process//Unsure what to do about this on linux.//TODO
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
						var nextQuote = target.IndexOf('"', 1);

						if (nextQuote > 0)
						{
							parsedArgs = target.Substring(nextQuote + 1).Trim();
							target = target.Substring(0, nextQuote + 1).Trim();
						}
						else
							target += '"';//Add the quote because it was missing, which is very unlikely.
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

							if (System.IO.Path.Exists(temp))
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
							var nextQuote = shellAction.IndexOf('"', 1);

							if (nextQuote > 0)
							{
								shellParams = shellAction.Substring(nextQuote + 1).Trim();
								shellAction = shellAction.Substring(0, nextQuote + 1).Trim();
								parsedArgs = shellParams;
							}
							else
								shellAction += '"';//Add the quote because it was missing, which is very unlikely.
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

								if (System.IO.Path.Exists(temp))
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

				if (!string.IsNullOrEmpty(showMode))
				{
					switch (showMode.ToLowerInvariant())
					{
						case Keyword_Max: prc.StartInfo.WindowStyle = ProcessWindowStyle.Maximized; break;

						case Keyword_Min: prc.StartInfo.WindowStyle = ProcessWindowStyle.Minimized; break;

						case Keyword_Hide: prc.StartInfo.WindowStyle = ProcessWindowStyle.Hidden; break;
					}
				}

				if (prc.Start())
				{
					pid = prc.Id;

					if (wait)
					{
                        prc.WaitForExit();
						Script.SetPropertyValue(outputVarPID, "__Value", pid);
						return prc.ExitCode;
					}
				}
			}
			catch (Exception ex)
			{
				ThreadAccessors.A_LastError = Marshal.GetLastSystemError();
				return (long)Errors.ErrorOccurred(ex.Message, DefaultErrorLong);
			}

            Script.SetPropertyValue(outputVarPID, "__Value", pid);
            return 0L;
		}
	}

	/// <summary>
	/// Encapsulates information and I/O for a spawned <see cref="Process"/>.
	/// </summary>
	public class ProcessInfo : KeysharpObject
	{
		private Process _process;
		public ProcessInfo(params object[] args) : base(args) { }

		public override object __New(params object[] args)
		{
			_process = args[0] as Process;
			return DefaultObject;
		}

		public long HasExited => _process.HasExited ? 1L : 0L;
		public long ExitCode => (long)_process.ExitCode;
		/// <summary>
		/// Gets the exit time formatted as "YYYYMMDDHH24MISS".
		/// </summary>
		public string ExitTime => Conversions.ToYYYYMMDDHH24MISS(_process.ExitTime);
		/// <summary>
		/// Returns a KeysharpFile wrapping the standard output stream.
		/// </summary>
		private object _StdOut = null;
		public object StdOut => _StdOut ??= new KeysharpFile(_process.StandardOutput);
		/// <summary>
		/// Returns a KeysharpFile wrapping the standard error stream.
		/// </summary>
		private object _StdErr = null;
		public object StdErr => _StdErr ??= new KeysharpFile(_process.StandardError);
		/// <summary>
		/// Returns a KeysharpFile wrapping the standard input stream.
		/// </summary>
		private object _StdIn = null;
		public object StdIn => _StdIn ??= new KeysharpFile(_process.StandardInput);
		/// <summary>
		/// Immediately kills the underlying process.
		/// </summary>
		/// <returns></returns>
		public object Kill()
		{
			_process.Kill();
			return DefaultObject;
		}
	}
}