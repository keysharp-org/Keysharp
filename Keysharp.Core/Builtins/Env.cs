namespace Keysharp.Builtins
{
#if !WINDOWS
	internal sealed class EnvData
	{
		// EnvUpdate is deliberately deferred, and neither platform exposes the process-environment delta.
		// Keep only changes made through EnvSet rather than diffing or republishing the inherited environment.
		private readonly Lock pendingChangesLock = new ();
		private readonly Dictionary<string, string> pendingChanges = new (StringComparer.Ordinal);

		internal void RecordChange(string name, string value)
		{
			lock (pendingChangesLock)
				pendingChanges[name] = value;
		}

		internal Dictionary<string, string> SnapshotPendingChanges()
		{
			lock (pendingChangesLock)
				return new (pendingChanges, pendingChanges.Comparer);
		}

		internal void AcknowledgePublishedChanges(IReadOnlyDictionary<string, string> publishedChanges)
		{
			lock (pendingChangesLock)
			{
				foreach (var pair in publishedChanges)
				{
					// EnvSet may run while EnvUpdate is publishing. Only remove the snapshot value;
					// a newer value for the same name must remain pending for the next call.
					if (pendingChanges.TryGetValue(pair.Key, out var current)
							&& string.Equals(current, pair.Value, StringComparison.Ordinal))
						_ = pendingChanges.Remove(pair.Key);
				}
			}
		}
	}
#endif

	/// <summary>
	/// Public interface for environment-related functions.
	/// </summary>
	public static class Env
	{

		/// <summary>
		/// Waits until the clipboard contains data.
		/// </summary>
		/// <param name="timeout">If omitted, the function will wait indefinitely. Otherwise, it will wait no longer than this many seconds.<br/>
		/// To wait for a fraction of a second, specify a floating-point number, for example, 0.25 to wait for a maximum of 250 milliseconds.
		/// </param>
		/// <param name="waitFor">If omitted, it defaults to 0 (wait only for text or files).<br/>
		/// Otherwise, specify one of the following numbers to indicate what to wait for:<br/>
		/// 0: The function is more selective, waiting specifically for text or files to appear("text" includes anything that would produce text when you paste into Notepad).<br/>
		/// 1: The function waits for data of any kind to appear on the clipboard.<br/>
		/// Keysharp additionally accepts a kind name — "Text", "Any", "Image", "Files", "Html" or "Rtf" — which is
		/// what <c>Clipboard.Wait</c> exposes; the numeric forms are unchanged.
		/// </param>
		/// <returns>True if it did not time out, else false.</returns>
		public static bool ClipWait(object timeout = null, object waitFor = null)
		{
			var to = timeout.Ad(double.MinValue);
			var condition = ParseWaitCondition(waitFor);
			var checktime = to != double.MinValue;
			long frequency = 100;
			var timeoutMs = checktime ? (long)(Math.Abs(to) * 1000) : long.MaxValue;
			var deadline = checktime ? Environment.TickCount64 + timeoutMs : long.MaxValue;

			while (true)
			{
				if (condition())
					return true;

				if (checktime)
				{
					var remaining = deadline - Environment.TickCount64;

					if (remaining <= 0)
						return false;

					_ = Flow.Sleep(Math.Min(frequency, remaining));
				}
				else
					_ = Flow.Sleep(frequency);
			}
		}

		/// <summary>
		/// Resolves <c>waitFor</c> ONCE, into the predicate the poll loop re-tests. The per-platform tests this
		/// replaced disagreed with each other — the Windows one asked for text-or-files, the Eto one for
		/// text-or-html-or-uris — where AHK's own rule is a single <c>CF_NATIVETEXT || CF_HDROP</c> check, which is
		/// exactly what <c>ChangeType() == 1</c> now means on every backend.
		/// </summary>
		private static Func<bool> ParseWaitCondition(object waitFor)
		{
			var clip = Platform.Clipboard;

			if (waitFor is string s && s.Length != 0 && !double.TryParse(s, out _))
			{
				if (string.Equals(s, "Any", StringComparison.OrdinalIgnoreCase))
					return () => !clip.IsEmpty;

				if (Conversions.ConvertClipboardKind(s) is { } kind)
					return () => clip.HasKind(kind);

				_ = Errors.ValueErrorOccurred($"Invalid clipboard wait kind: {s}");
			}

			return waitFor.Ab() ? () => !clip.IsEmpty : () => clip.ChangeType() == 1;
		}

		/// <summary>
		/// Retrieves the value of the specified environment variable.
		/// </summary>
		/// <param name="envVar">The name of the environment variable to retrieve.</param>
		/// <returns>The value of the specified environment variable if it exists, else empty string.</returns>
		public static string EnvGet(object envVar) => Environment.GetEnvironmentVariable(envVar.As()) ?? string.Empty;

		/// <summary>
		/// Writes a value to the specified environment variable.
		/// </summary>
		/// <param name="envVar">The name of the environment variable.</param>
		/// <param name="value">If omitted, the environment variable will be deleted. Otherwise, specify the value to write.</param>
		/// <exception cref="OSError">An <see cref="OSError"/> exception is thrown if any failure is detected.</exception>
		public static object EnvSet(object envVar, object value = null)
		{
			try
			{
				var variableName = envVar.As();
				var variableValue = value as string;
				Environment.SetEnvironmentVariable(variableName, variableValue);
#if !WINDOWS
				Script.TheScript?.EnvData.RecordChange(variableName, variableValue);
#endif
				return DefaultObject;
			}
			catch (Exception ex)
			{
				return Errors.OSErrorOccurred(ex, $@"Error setting environment variable {envVar} to value ""{value}"".");
			}
		}

		/// <summary>
		/// Notifies the operating system that environment variables have changed. On Linux and macOS, changes
		/// made by EnvSet are published to the current user's service manager for subsequently launched processes.
		/// </summary>
		/// <exception cref="OSError">An <see cref="OSError"/> exception is thrown if any failure is detected. A
		/// session manager that simply isn't installed is not a failure — there is nowhere to publish to, so that
		/// is logged and the call succeeds.</exception>
		public static object EnvUpdate()
		{
#if LINUX || OSX
			var envData = Script.TheScript?.EnvData;
			var pendingChanges = envData?.SnapshotPendingChanges() ?? new Dictionary<string, string>();

			if (pendingChanges.Count == 0)
				return DefaultObject;

			foreach (var command in BuildEnvironmentUpdateCommands(pendingChanges))
			{
				var result = RunCommand(command.FileName, command.Arguments);

				if (result.Succeeded)
					continue;

				// A session manager that isn't installed at all (no D-Bus activation environment, no systemd
				// user manager, no launchd) has nowhere to publish to. That is a missing platform feature
				// rather than a failed update, so it is logged; a helper that ran and reported an error is a
				// real failure and is raised.
				if (!result.Started)
				{
					Diagnostics.Debug.WriteLine($"EnvUpdate: {command.FileName} is unavailable, so the environment changes " +
									   $"were not published to the session manager. Details: {result.ErrorMessage}");
					continue;
				}

				return Errors.OSErrorOccurred(new InvalidOperationException(result.ErrorMessage),
						$"EnvUpdate failed while running {command.FileName}.");
			}

			envData?.AcknowledgePublishedChanges(pendingChanges);
#elif WINDOWS

			//SendMessage() freezes when running in a unit test. PostMessage seems to work. Use SendMessageTimeout().
			try { _ = WindowsAPI.SendMessageTimeout(new nint(WindowsAPI.HWND_BROADCAST), WindowsAPI.WM_SETTINGCHANGE, 0, 0, SendMessageTimeoutFlags.SMTO_ABORTIFHUNG, 1000, out var result); }
			catch (Exception ex)
			{
				return Errors.OSErrorOccurred(ex, "Error updating environment variables.");
			}
#else
			return Errors.OSErrorOccurred(new PlatformNotSupportedException(), "EnvUpdate is not supported on this platform.");
#endif
			return DefaultObject;
		}

#if !WINDOWS
		internal readonly record struct EnvironmentUpdateCommand(string FileName, string[] Arguments);

		internal static List<EnvironmentUpdateCommand> BuildEnvironmentUpdateCommands(IReadOnlyDictionary<string, string> changes)
		{
			var commands = new List<EnvironmentUpdateCommand>();
			var ordered = changes.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray();
#if LINUX
			var dbusArguments = new List<string> { "--systemd" };

			foreach (var pair in ordered)
				// dbus-update-activation-environment cannot remove a variable, so deletion is
				// represented as an empty value there and removed exactly from systemd below.
				dbusArguments.Add($"{pair.Key}={pair.Value ?? string.Empty}");

			commands.Add(new ("dbus-update-activation-environment", dbusArguments.ToArray()));
			var deletedNames = ordered.Where(pair => pair.Value == null).Select(pair => pair.Key).ToArray();

			if (deletedNames.Length > 0)
				commands.Add(new ("systemctl", ["--user", "unset-environment", .. deletedNames]));
#elif OSX
			foreach (var pair in ordered)
				commands.Add(pair.Value == null
					? new ("/bin/launchctl", ["unsetenv", pair.Key])
					: new ("/bin/launchctl", ["setenv", pair.Key, pair.Value]));
#endif
			return commands;
		}

#endif

		/// <summary>
		/// Assign command line arguments to <see cref="A_Args"/>.
		/// This should never be called directly by a Script.TheScript.<br/>
		/// Instead, it's used by the parser in the generated C# code.
		/// </summary>
		/// <param name="args">The command line arguments to process.</param>
		[PublicHiddenFromUser]
		public static object HandleCommandLineParams(string[] args)
		{
			if (args.Length > 0 && args[0].TrimStart(Keywords.DashSlash).ToUpper() == "SCRIPT")
			{
				string[] newArgs = new string[args.Length - 1];
				System.Array.Copy(args, 1, newArgs, 0, args.Length - 1);
				var command = Runner.Parse(newArgs);

				if (command.RequiresLauncher)
					throw new Exception("This option is only available from the Keysharp launcher, not from a compiled script: --compile exe/exe-min, --daemon, --install, --uninstall.");

				Environment.ExitCode = Runner.Execute(command);
				throw new Flow.UserRequestedExitException();
			}

			A_Args.array.AddRange(args);
			return DefaultObject;
		}

		/// <summary>
		/// Registers a function to be called automatically whenever the clipboard's content changes.
		/// </summary>
		/// <param name="callback">The callback to call, which has a single parameter specifying which data type the clipboard contains.<br/>
		/// 0: Clipboard is now empty.<br/>
		/// 1: Clipboard contains something that can be expressed as text(this includes files copied from an Explorer window).<br/>
		/// 2: Clipboard contains something entirely non-text such as a picture.<br/>
		/// </param>
		/// <param name="addRemove">If omitted, it defaults to 1. Otherwise, specify one of the following numbers:<br/>
		///  1: Call the callback after any previously registered callbacks.<br/>
		/// -1: Call the callback before any previously registered callbacks.<br/>
		///  0: Do not call the callback.
		/// </param>
		/// <exception cref="TypeError">A <see cref="TypeError"/> exception is thrown if callback is not of type <see cref="KeysharpFunc"/>.</exception>
			public static object OnClipboardChange(object callback, object addRemove = null)
			{
				if (callback is KeysharpFunc fo)
				{
					var script = Script.TheScript;
					if (script.ClipFunctions.ModifyEventHandlers(fo, addRemove.Al(1)))
						script.UpdateClipboardMonitoring();

					return DefaultObject;
				}
				else
				return Errors.TypeErrorOccurred(callback, typeof(KeysharpFunc), DefaultObject);
		}

		/// <summary>
		/// Retrieves dimensions of system objects, and other system properties.
		/// </summary>
		/// <param name="property">The variable to store the result.</param>
		/// <returns>This function returns the value of the specified system property.</returns>
		public static object SysGet(object property)
		{
#if !WINDOWS
			var sm = property is Keysharp.Internals.Os.SystemMetric en ? en : (SystemMetric)property.Ai();
			var (screenWidth, screenHeight) = Monitor.GetPrimaryScreenSize();
			var (workWidth, workHeight) = Monitor.GetPrimaryWorkAreaSize();

			switch (sm)
			{
				case SystemMetric.SM_CMONITORS:
					return Monitor.MonitorGetCount();

				case SystemMetric.SM_CXSCREEN:
					return screenWidth;

				case SystemMetric.SM_CYSCREEN:
					return screenHeight;

				case SystemMetric.SM_CXFULLSCREEN:
					return workWidth;

				case SystemMetric.SM_CYFULLSCREEN:
					return workHeight;

				case SystemMetric.SM_CXMAXIMIZED:
					return workWidth;

				case SystemMetric.SM_CYMAXIMIZED:
					return workHeight;

				case SystemMetric.SM_MOUSEPRESENT:
					return 1L;

				case SystemMetric.SM_SWAPBUTTON:
					return MouseButtonsSwapped() ? 1L : 0L;

				case SystemMetric.SM_CMOUSEBUTTONS:
					return MouseButtonCount();

				case SystemMetric.SM_NETWORK:
					return NetworkUp() ? 1L : 0L;

				case SystemMetric.SM_CLEANBOOT:
				{
					_ = "last reboot".Bash(out var bootsOutput);
					var boots = bootsOutput.SplitLines().ToList();

					if (boots.Count > 0)
					{
						if (boots[0].Contains("recovery", StringComparison.OrdinalIgnoreCase))
							return NetworkUp() ? 2L : (object)1L;
					}

					return 0L;
				}

				case SystemMetric.SM_MOUSEWHEELPRESENT:
					return "xinput --list --long".Bash(out var wheelOut) == 0
						&& wheelOut.Contains("button wheel", StringComparison.OrdinalIgnoreCase) ? 1L : 0L;

				case SystemMetric.SM_REMOTESESSION:
					return "echo $SSH_TTY".Bash(out var sshOut1) == 0 && sshOut1 != "" ? 1L : 0L;

				case SystemMetric.SM_SHUTTINGDOWN:
					return "systemctl is-system-running".Bash(out var systemStateOut) == 0
						&& systemStateOut.Contains("stopping", StringComparison.OrdinalIgnoreCase) ? 1L : 0L;

				case SystemMetric.SM_REMOTECONTROL:
					return "echo $SSH_TTY".Bash(out var sshOut2) == 0 && sshOut2 != "" ? 1L : 0L;

				default:
					throw new NotImplementedException($"SysGet({sm}) has no Linux/Eto equivalent.");
			}
#elif WINDOWS

			if (property is SystemMetric en)
				return (long)WindowsAPI.GetSystemMetrics(en);

			return (long)WindowsAPI.GetSystemMetrics((SystemMetric)property.Ai());
#else
			return 0L;
#endif
		}

		internal static byte[] ExtractClipboardAllBytes(object data, long size = long.MinValue)
		{
			if (data == null)
				return System.Array.Empty<byte>();

			if (data is byte[] ba)
				return CopyClipboardBytes(ba, size);

			if (data is Array arr)
				return CopyClipboardBytes(arr.ToByteArray().ToArray(), size);

			if (Reflections.TryGetPtrProperty(data, out var ptr))
			{
				var sourceLength = size;

				if (sourceLength == long.MinValue)
					_ = Reflections.TryGetSizeProperty(data, out sourceLength);//0/false when no Size; the > 0 guard below handles it.

				if (sourceLength > 0)
					return CopyClipboardBytes((nint)ptr, sourceLength, size);
			}

			return System.Array.Empty<byte>();
		}

		private static byte[] CopyClipboardBytes(byte[] source, long requestedSize = long.MinValue)
		{
			if (source == null || source.Length == 0)
				return System.Array.Empty<byte>();

			var length = requestedSize == long.MinValue ? source.Length : (int)Math.Max(0, Math.Min(requestedSize, source.Length));
			var result = new byte[length];
			System.Array.Copy(source, result, length);
			return result;
		}

		private static byte[] CopyClipboardBytes(nint sourcePtr, long sourceLength, long requestedSize = long.MinValue)
		{
			if (sourcePtr == 0 || sourceLength <= 0)
				return System.Array.Empty<byte>();

			var length = requestedSize == long.MinValue ? sourceLength : Math.Max(0, Math.Min(sourceLength, requestedSize));

			if (length <= 0 || length > int.MaxValue)
				return System.Array.Empty<byte>();

			var result = new byte[length];
			Marshal.Copy(sourcePtr, result, 0, (int)length);
			return result;
		}

		/// <summary>
		/// Internal helper to search the command line arguments for a specified string.
		/// </summary>
		/// <param name="arg">The argument to search for.</param>
		/// <param name="startsWith">True to require the argument to start with arg, else it must contain arg.</param>
		/// <returns>The matched argument if found, else null.</returns>
		internal static string FindCommandLineArg(string arg, bool startsWith = true)
		{
			// May be queried before a Script exists (e.g. compiler-error reporting during early argument
			// parsing), in which case there are no Keysharp args to search.
			var args = Script.TheScript?.KeysharpArgs;

			if (args == null)
				return null;

			if (startsWith)
				return args.FirstOrDefault(x => (x.StartsWith('-')
						|| x.StartsWith('/')) && x.Trim(DashSlash).StartsWith(arg, StringComparison.OrdinalIgnoreCase));
			else
				return args.FirstOrDefault(x => (x.StartsWith('-')
						|| x.StartsWith('/')) && x.Trim(DashSlash).Contains(arg, StringComparison.OrdinalIgnoreCase));
		}

		/// <summary>
		/// Internal helper to search the command line argument values for a specified string.
		/// </summary>
		/// <param name="arg">The argument to search for.</param>
		/// <param name="startsWith">True to require the argument to start with arg, else it must contain arg.</param>
		/// <returns>The matched argument if found, else null.</returns>
		/// <returns>The matched value if found, else null.</returns>
		internal static string FindCommandLineArgVal(string arg, bool startsWith = true)
		{
			var args = Script.TheScript?.KeysharpArgs;

			if (args == null)
				return null;

			for (var i = 0; i < args.Length; i++)
			{
				if ((args[i].StartsWith('-') || args[i].StartsWith('/')) && (startsWith ? args[i].TrimStart(DashSlash).StartsWith(arg, StringComparison.OrdinalIgnoreCase) : args[i].Contains(arg, StringComparison.OrdinalIgnoreCase)))
					if (i < args.Length - 1)
						return args[i + 1];
			}

			return null;
		}

#if !WINDOWS
		/// <summary>
		/// Get the number of buttons on the mouse.
		/// This tries to find the device with the least number of buttons and assumes that is the mouse.
		/// XTEST devices are considered but it shouldn't matter because they won't have less buttons than
		/// the actual mouse.
		/// </summary>
		/// <returns>The number of mouse buttons detected</returns>
		internal static long MouseButtonCount()
		{
#if LINUX
			var count = long.MaxValue;
			if ("xinput list --long".Bash(out var inputStr) != 0)
				return 3L;

			foreach (Range r in inputStr.AsSpan().SplitAny(CrLf))
			{
				var split = inputStr.AsSpan(r).Trim();

				if (split.Contains("Buttons supported:", StringComparison.OrdinalIgnoreCase))
				{
					var splitct = 0;

					foreach (Range r2 in split.Split(':'))
					{
						var btnSplit = split[r2].Trim();

						if (btnSplit.Length > 0)
						{
							if (splitct > 0)
							{
								if (long.TryParse(btnSplit, out var btnCount))
								{
									count = Math.Min(count, btnCount);
									break;
								}
							}

							splitct++;
						}
					}
				}
			}

			return count == long.MaxValue ? 3L : count;
#else
			return 3L;
#endif
		}

		/// <summary>
		/// Whether the primary and secondary mouse buttons are swapped, for <c>SysGet(SM_SWAPBUTTON)</c>.
		/// <para>
		/// This defers to the active <see cref="KeyboardMouseSender"/>'s own answer rather than probing the system
		/// separately, so the value a script reads always matches the one the send path acts on. The two used to be
		/// independent: this method shelled out to <c>xinput list</c> plus one <c>xinput get-button-map</c> per device
		/// while the sender read <c>XGetPointerMapping</c>, so they could disagree on X11 (different device sets), the
		/// shell-out saw only XWayland devices on a Wayland session, and every call spawned 2+N processes.
		/// </para>
		/// <para>
		/// Per-backend accuracy is therefore whatever the sender reports: X11 reads the pointer map, while the native
		/// Wayland/inputd and macOS senders currently answer false unconditionally. That is a pre-existing gap rather
		/// than a regression here — consolidating removes a second, differently-wrong answer, and closing the
		/// remaining one belongs in the senders, where the send path picks it up at the same time.
		/// </para>
		/// </summary>
		/// <returns>Whether the mouse buttons are swapped; false before any sender has been created.</returns>
		internal static bool MouseButtonsSwapped()
			=> Script.TheScript.HookThread?.kbdMsSender?.MouseButtonsSwapped ?? false;

		internal static bool NetworkUp()
		{
#if LINUX
			return "ip link show".Bash(out var linkStateOut) == 0
				&& linkStateOut.Contains("state up", StringComparison.OrdinalIgnoreCase);
#else
			_ = "ifconfig".Bash(out var output);
			return output.Contains("status: active", StringComparison.OrdinalIgnoreCase);
#endif
		}

#endif

	}
	/// <summary>
	/// A class that represents clipboard data.
	/// This is just a thin derivation of <see cref="Buffer"/>.
	/// </summary>
	public class ClipboardAll : Buffer
	{
		/// <summary>
		/// Constructor that just passes the data to the base.
		/// </summary>
		/// <param name="obj">The data to pass to the base.</param>
		//public ClipboardAll(byte[] obj)
		//  : base(obj)
		//{
		//}

		// `new`, not `override`: construction dispatches by name, so the real signature is declared here and
		// arity/defaults/named binding follow from it (see Buffer.__New and Any's constructor).
		public new object __New(object Data = null, object Size = null)
		{
			var bytes = Data == null
						? Platform.Clipboard.CaptureAll()
						: Env.ExtractClipboardAllBytes(Data, Size is not null ? Size.ToLong() : long.MinValue);

			//Passed as the single ByteCount argument, not wrapped in an object[]: Buffer.__New now declares real
			//parameters, so the byte[] must arrive as the argument itself (it has a dedicated branch there).
			return base.__New(bytes);
		}

		public ClipboardAll(params object[] args) : base(args) { }
	}

	public partial class Ks
	{
		/// <summary>Returns whether an optional scripting capability is installed or embedded.</summary>
		public static object ComponentAvailable(object capability)
		{
			var name = capability.As().Replace("-", "", StringComparison.Ordinal)
				.Replace("_", "", StringComparison.Ordinal).ToUpperInvariant();
			var parsed = name switch
			{
				"PARSER" or "PARSING" => Keysharp.Components.Scripting.ScriptingCapability.SyntaxValidation,
				"COMPILER" or "COMPILATION" => Keysharp.Components.Scripting.ScriptingCapability.Compilation,
				_ => Keysharp.Components.Scripting.ScriptingCapability.None,
			};

			return parsed == Keysharp.Components.Scripting.ScriptingCapability.None
				? Errors.ValueErrorOccurred($"Unknown scripting component capability: {capability}")
				: ScriptingComponentRegistry.IsAvailable(parsed);
		}

		/// <summary>
		/// RunScript's Options as individual command-line arguments: an Array element by element, or a string split on
		/// whitespace with double quotes grouping, so `--include "My include.ahk"` stays one argument. Written out
		/// because there is nothing to borrow — .NET exposes no command-line splitter, and the repo's quote-aware
		/// splitters parse expressions (they also track paren/brace nesting), not command lines. The rule is
		/// deliberately just "quotes group, and are removed"; anything needing more should use the Array form, where
		/// each element is one argument and nothing has to be escaped at all.
		/// </summary>
		private static List<string> SplitCommandLine(object options)
		{
			var args = new List<string>();

			// Looped rather than LINQ: Keysharp's Array implements two IEnumerable<T> instantiations, so Select is
			// ambiguous on it (see the note on Array.GetEnumerator).
			if (options is Array arr)
			{
				foreach (var v in arr)
					if (v?.As() is { Length: > 0 } s)
						args.Add(s);

				return args;
			}

			var sb = new StringBuilder();
			var quoted = false;
			// Distinguishes `--x ""`, a deliberate empty argument, from the whitespace between two arguments — without
			// it the empty one is silently dropped and every later argument shifts up one position.
			var started = false;

			foreach (var c in options.As())
			{
				if (c == '"') { quoted = !quoted; started = true; }
				else if (quoted || !char.IsWhiteSpace(c)) { _ = sb.Append(c); started = true; }
				else if (started) { args.Add(sb.ToString()); _ = sb.Clear(); started = false; }
			}

			// An unterminated quote takes the rest of the line, which is what was visibly written minus the closer.
			if (started)
				args.Add(sb.ToString());

			return args;
		}

		/// <summary>
		/// Compiles and executes a C# script dynamically in a separate process.
		/// </summary>
		/// <param name="code">The script source result (as any object with a valid string representation).</param>
		/// <param name="callbackOrAsync">Whether to run the process as async (provide non-unset non-zero value) or not.
		/// <param name="name">An optional name for the dynamically generated program; defaults to "*".</param>
		/// <param name="executable">Optional executable path used to run the generated assembly; defaults to the currently running process.</param>
		/// If provided a callback function then it's considered async and the function <c>Call</c> method will be
		/// invoked when the process exits with the ProcessInfo as the only argument.</param>
		/// <param name="options">Optional Keysharp command-line arguments for this script, either as a string
		/// (e.g. `--define:FEATURE_X --include "My include.ahk"`, split on whitespace with double quotes grouping) or
		/// as an Array where each element is already one argument. Nothing is inherited from the calling script: this
		/// is a separate compilation in a separate process, so it states what it wants. `--define:NAME` is applied to
		/// the compile that happens HERE, since the launched process receives already-compiled bytes and a define
		/// handed to it would arrive after the conditionals were resolved; every other argument goes to that process.</param>
		/// <returns>
		/// Returns a <see cref="ProcessInfo"/> wrapper around the spawned process.
		/// If compilation fails without a flagged error, returns <c>null</c>.
		/// </returns>
		/// <exception cref="Error">Throws any compilation as <see cref="Error"/>.</exception>
		public static object RunScript(object code, object callbackOrAsync = null, object name = null, object executable = null, object options = null)
		{
			string script = code.As();
			KeysharpFunc cb = null;

			if (callbackOrAsync != null)
				cb = Functions.Func(callbackOrAsync);

			string nameVal = name?.As();
			// --define selects which code is COMPILED, and that happens here — the launched process only ever receives
			// already-compiled bytes, so a define forwarded to it would arrive too late to mean anything. Every other
			// argument is genuinely the launched process's, and is passed along below.
			List<string> defineNames = null, forwardedArgs = null;

			if (options != null && Runner.SplitDefines(SplitCommandLine(options), out defineNames, out forwardedArgs) is string badDefine)
				return Errors.ValueErrorOccurred(badDefine);

			string result = null;
			byte[] compiledBytes = null;
			string compiledPath = null;
			var ext = Path.GetExtension(script);

			// A precompiled assembly file (.cks/.dll) is run as-is; only source needs compiling. This lets
			// callers ship and launch a precompiled script (e.g. WindowSpy.cks) for faster startup.
			if (File.Exists(script) && (ext.Equals(".cks", StringComparison.OrdinalIgnoreCase) || ext.Equals(".dll", StringComparison.OrdinalIgnoreCase)))
			{
				if (defineNames is { Count: > 0 })
					return Errors.ValueErrorOccurred("--define cannot be applied to an already-compiled .cks/.dll script; its conditionals were resolved when it was built.");

				compiledPath = Path.GetFullPath(script);
			}
			else
			{
				if (!ScriptingComponentRegistry.TryGetCompiler(out var compiler, out var componentFailure))
					return Errors.ErrorOccurred(componentFailure);

				var compilation = compiler.Compile(new Keysharp.Components.Scripting.ScriptCompileRequest
				{
					SourceText = File.Exists(script) ? null : script,
					ScriptPath = File.Exists(script) ? script : null,
					CompilationName = nameVal,
					RuntimeDirectory = Path.GetDirectoryName(Ks.A_KeysharpCorePath),
					Defines = defineNames ?? [],
					Output = Keysharp.Components.Scripting.ScriptCompilationOutput.InMemory,
				});
				compiledBytes = compilation.AssemblyBytes;
				result = compilation.ErrorText;

				if (!compilation.Success)
					return Errors.ErrorOccurred(result);

				// Non-fatal #Warning text from the nested compile; the caller's console is the only place to show it.
				if (!string.IsNullOrEmpty(compilation.WarningText))
					Console.Error.WriteLine(compilation.WarningText);
			}

			// Relaunch a Keysharp host that understands "--script --assembly" with either an assembly path
			// or bytes piped as "*". Environment.ProcessPath is only our app when published; under the dotnet
			// host (e.g. "dotnet Keysharp.dll" while debugging from an IDE) it is "dotnet", and handing
			// Keysharp's args to dotnet makes it exit without reading stdin -- so the pipe write below
			// would time out. Prefer the native apphost that sits beside the entry assembly; fall back to
			// "dotnet <entry.dll>", and finally to ProcessPath (single-file publish has no separate dll).
			string launcher = executable?.As();
			// The caller's arguments go BEFORE "--script --assembly *": the command line is read as switches, then the
			// script, then the script's own arguments, so anything after the script marker would be taken for the
			// latter. Collected as a LIST, not joined into one string: ProcessStartInfo.ArgumentList applies the
			// platform's own quoting, so an argument containing a space, a tab, a double quote or a trailing backslash
			// arrives intact. Hand-joining cannot get this right — on Windows the child's parser reads the `\"` in
			// "C:\My Dir\" as an escaped quote, swallowing the rest of the command line.
			var launcherArgs = new List<string>();

			if (forwardedArgs != null)
				launcherArgs.AddRange(forwardedArgs);

			launcherArgs.AddRange(["--script", "--assembly", compiledPath ?? "*"]);

			if (string.IsNullOrEmpty(launcher))
			{
#if WINDOWS
				const string appHostExtension = ".exe";
#else
				const string appHostExtension = "";
#endif
				var entryAsm = Assembly.GetEntryAssembly()?.Location;
				var entryDir = string.IsNullOrEmpty(entryAsm) ? null : Path.GetDirectoryName(entryAsm);
				var appHost = entryDir == null ? null
							  : Path.Combine(entryDir, Path.GetFileNameWithoutExtension(entryAsm) + appHostExtension);

				if (appHost != null && File.Exists(appHost))
					launcher = appHost;
				else if (entryDir != null && string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase))
				{
					// "dotnet <entry.dll> …" — the assembly to run must precede everything else.
					launcher = Environment.ProcessPath;
					launcherArgs.Insert(0, entryAsm);
				}
				else
					launcher = Environment.ProcessPath;
			}

			var scriptProcess = new Process
			{
				StartInfo = new ProcessStartInfo
				{
					FileName = launcher,
					RedirectStandardInput = compiledPath == null,
					RedirectStandardOutput = true,
					UseShellExecute = false,
					CreateNoWindow = true
				}
			};

			foreach (var arg in launcherArgs)
				scriptProcess.StartInfo.ArgumentList.Add(arg);

			var info = new ProcessInfo(scriptProcess);
			scriptProcess.EnableRaisingEvents = true;
			scriptProcess.Exited += (object sender, EventArgs e) => cb?.Call(info);
			_ = scriptProcess.Start();

			// Source compiled in this process is still transferred as raw assembly bytes. A precompiled file is
			// passed by path so its script identity and adjacent optional-component search root survive the launch.
			if (compiledPath == null)
			{
				using var stdin = scriptProcess.StandardInput.BaseStream;
				stdin.Write(compiledBytes, 0, compiledBytes.Length);
				stdin.Flush();
			}

			if (!ForceBool(callbackOrAsync ?? false))
				scriptProcess.WaitForExit();

			return info;
		}

		///
		/// <summary>
		/// Parses, lowers, and validates the provided script source or filename with the compiler component.
		/// On success this method returns <c>""</c>. On failure it returns a string containing
		/// the formatted compiler errors.
		/// </summary>
		/// <param name="code">The script source or filename to parse.</param>
		/// <returns>
		/// Returns <see cref=""/> when parsing completes with no compiler errors and a valid compilation unit.
		/// If the compiler reports errors or the first compilation unit is null, a string containing compiler error messages
		/// (and warnings if present) is returned.
		/// </returns>
		/// <exception cref="Exception">
		/// Any unexpected exception thrown by the installed compiler component will propagate to the caller.
		/// </exception>
		public static object ParseScript(object code)
		{
			if (!ScriptingComponentRegistry.TryGetCompiler(out var compiler, out var failure))
				return failure;

			var source = code.As();
			var isFile = File.Exists(source);
			var result = compiler.Compile(new Keysharp.Components.Scripting.ScriptCompileRequest
			{
				SourceText = isFile ? null : source,
				ScriptPath = isFile ? source : null,
				CompilationName = isFile ? Path.GetFileNameWithoutExtension(source) : "ParseScript",
				RuntimeDirectory = Path.GetDirectoryName(Ks.A_KeysharpCorePath),
				Output = Keysharp.Components.Scripting.ScriptCompilationOutput.InMemory,
				AllowPackageRestore = false,
			});
			return result.Success ? "" : result.ErrorText;
		}
	}
}
