namespace Keysharp.Builtins
{
	internal class AccessorData
	{
		internal long hotkeyModifierTimeout = 50L;
		internal long hotkeyThrottleInterval = 2000L;
		internal long maxHotkeysPerInterval = 2000L;
		internal readonly string initialWorkingDir = Environment.CurrentDirectory;
		internal bool allowMainWindow = true;
#if WINDOWS
		internal string guiTheme = "Classic";
#else
		internal string guiTheme = "System";
#endif
		internal bool? iconFrozen;
		internal string iconFile = "";
		internal bool iconHidden;
		internal object iconNumber = 1L;
		internal string iconTip;
		internal long inputLevel;
		internal string menuMaskKey = "";
		internal long clipboardTimeout = 1000L;
		internal long hotIfTimeout = Accessors.DefaultHotIfTimeout;
		internal bool maxThreadsBuffer;
		internal uint maxThreadsPerHotkey = 1u;
#if WINDOWS
		internal Icon prevTrayIcon;
#else
		internal Image prevTrayIcon;
#endif
		internal ThreadConfigData threadConfigDataPrototype = new(); // Used (and set by) the auto-execute section
	}

	/// <summary>
	/// Public static properties which mostly start with A_.
	/// </summary>
	public static class Accessors
	{
		internal const long DefaultHotIfTimeout = 250L;


		/// <summary>
		/// The full path of the assembly that is currently executing.
		/// </summary>
		public static string A_AhkPath =>
#if WINDOWS
		Application.ExecutablePath;
#else
		Environment.ProcessPath ?? "";
#endif

		/// <summary>
		/// Hard-coded AutoHotkey version which Keysharp should support
		/// </summary>
		public static string A_AhkVersion => "2.1-alpha.31";

		/// <summary>
		/// Whether the script's main window is allowed to be opened via the tray icon.
		/// </summary>
		public static object A_AllowMainWindow
		{
			get => Script.TheScript.AccessorData.allowMainWindow;

			set
			{
				var val = Options.OnOff(value);

				if (val.HasValue)
				{
					var script = Script.TheScript;
					script.AccessorData.allowMainWindow = val.Value;
					script.openMenuItem.Visible = val.Value;

					if (!A_AllowMainWindow.Ab())
						script.openMenuItem.Visible = false;
					else
						script.trayMenu.Default = "&Open";
				}
			}
		}

		/// <summary>
		/// The full path and name of the folder containing the current user's application-specific data.<br/>
		/// Windows: %APPDATA% (for example C:\Users\Username\AppData\Roaming)<br/>
		/// Linux: $XDG_CONFIG_HOME, else ~/.config<br/>
		/// macOS: ~/Library/Application Support
		/// </summary>
		// DoNotVerify for the same reason as A_MyDocuments: the default option returns "" for a folder that
		// does not exist yet, so on a fresh account paths built from this would resolve against the
		// filesystem root. It also keeps this a pure read - Create would mkdir on mere evaluation.
		public static string A_AppData =>
#if OSX
		Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support");
#else
		Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify);
#endif

		/// <summary>
		/// The full path and name of the folder containing the all-users application-specific data.<br/>
		/// Windows: %ProgramData%<br/>
		/// Linux: the first entry of $XDG_CONFIG_DIRS, else /etc/xdg<br/>
		/// macOS: /Library/Application Support
		/// </summary>
		// Config-shaped and admin-writable, the permission shape %ProgramData% has. Not .NET's
		// CommonApplicationData, which maps to /usr/share on both Unixes: package-manager territory.
		public static string A_AppDataCommon =>
#if WINDOWS
		Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData, Environment.SpecialFolderOption.DoNotVerify);
#elif OSX
		"/Library/Application Support";
#else
		UnixCommonConfigDir();
#endif
#if !WINDOWS && !OSX

		/// <summary>
		/// The machine-wide configuration root for <see cref="A_AppDataCommon"/>. $XDG_CONFIG_DIRS is a
		/// colon-separated search path in precedence order, so the first usable entry is the one an admin
		/// means; the spec's default is /etc/xdg, and a relative entry is invalid and skipped.
		/// </summary>
		private static string UnixCommonConfigDir()
		{
			var dirs = Environment.GetEnvironmentVariable("XDG_CONFIG_DIRS");

			if (!dirs.IsNullOrEmpty())
				foreach (var dir in dirs.Split(':', StringSplitOptions.RemoveEmptyEntries))
					if (Path.IsPathRooted(dir))
						return dir;

			return "/etc/xdg";
		}
#endif

		/// <summary>
		/// An <see cref="Array"/> containing the command line arguments used to run the script.
		/// </summary>
		public static Array A_Args { get; internal set; } = [];









		/// <summary>
		/// The data on the clipboard.
		/// </summary>
		public static object A_Clipboard
		{
			// Platform.Clipboard marshals to the UI thread itself (see PlatformHost.CreateClipboard), so this — like every
			// other clipboard seam — is a plain call. The backend behind it (Windows raw-Win32, a Wayland shell
			// extension, or Eto) was chosen once at startup; see LinuxClipboards.Resolve.
			get
			{
				ClipboardPermission.EnsureMonitoring("A_Clipboard");
				return Platform.Clipboard.GetText();
			}

			set
			{
				if (value is ClipboardAll arr)
					Platform.Clipboard.RestoreAll(arr);
				else
					Platform.Clipboard.SetText(value?.ToString() ?? "");
			}
		}


		/// <summary>
		/// The name of the computer as seen on the network.
		/// </summary>
		public static string A_ComputerName => Environment.MachineName;

#if WINDOWS

		/// <summary>
		/// Contains the contents of the COMSPEC environment variable, which is the full path to the command line executable.
		/// </summary>
		public static string A_ComSpec => Environment.GetEnvironmentVariable("COMSPEC");

#endif

		/// <summary>
		/// The delay in milliseconds that will occur after each control-modifying command.
		/// </summary>
		public static object A_ControlDelay
		{
			get => ThreadAccessors.A_ControlDelay;
			set => ThreadAccessors.A_ControlDelay = value.ToLong();
		}

		/// <summary>
		/// The coordinate mode for positioning the caret.
		/// </summary>
		/// <exception cref="ValueError">A <see cref="ValueError"/> exception is thrown if the value couldn't be converted to a <see cref="CoordModeType"/>.</exception>
		public static object A_CoordModeCaret
		{
			get => CoordModeTypeToString(ThreadAccessors.A_CoordModeCaret);

			set
			{
				var val = CoordModeType.Client;

				if (value is CoordModeType cmt)
					val = cmt;
				else if (Enum.TryParse(value.As(), out cmt))
					val = cmt;
				else
				{
					_ = Errors.ValueErrorOccurred($"{value} was not in the correct format for coordinate modes.");
					return;
				}

				ThreadAccessors.A_CoordModeCaret = val;
			}
		}

		/// <summary>
		/// The coordinate mode for positioning menus.
		/// </summary>
		/// <exception cref="ValueError">A <see cref="ValueError"/> exception is thrown if the value couldn't be converted to a <see cref="CoordModeType"/>.</exception>
		public static object A_CoordModeMenu
		{
			get => CoordModeTypeToString(ThreadAccessors.A_CoordModeMenu);

			set
			{
				var val = CoordModeType.Client;

				if (value is CoordModeType cmt)
					val = cmt;
				else if (Enum.TryParse(value.As(), out cmt))
					val = cmt;
				else
				{
					_ = Errors.ValueErrorOccurred($"{value} was not in the correct format for coordinate modes.");
					return;
				}

				ThreadAccessors.A_CoordModeMenu = val;
			}
		}

		/// <summary>
		/// The coordinate mode for positioning the mouse.
		/// </summary>
		/// <exception cref="ValueError">A <see cref="ValueError"/> exception is thrown if the value couldn't be converted to a <see cref="CoordModeType"/>.</exception>
		public static object A_CoordModeMouse
		{
			get => CoordModeTypeToString(ThreadAccessors.A_CoordModeMouse);

			set
			{
				var val = CoordModeType.Client;

				if (value is CoordModeType cmt)
					val = cmt;
				else if (Enum.TryParse(value.As(), out cmt))
					val = cmt;
				else
				{
					_ = Errors.ValueErrorOccurred($"{value} was not in the correct format for coordinate modes.");
					return;
				}

				ThreadAccessors.A_CoordModeMouse = val;
			}
		}

		/// <summary>
		/// The coordinate mode for positioning pixels.
		/// </summary>
		/// <exception cref="ValueError">A <see cref="ValueError"/> exception is thrown if the value couldn't be converted to a <see cref="CoordModeType"/>.</exception>
		public static object A_CoordModePixel
		{
			get => CoordModeTypeToString(ThreadAccessors.A_CoordModePixel);

			set
			{
				var val = CoordModeType.Client;

				if (value is CoordModeType cmt)
					val = cmt;
				else if (Enum.TryParse(value.As(), out cmt))
					val = cmt;
				else
				{
					_ = Errors.ValueErrorOccurred($"{value} was not in the correct format for coordinate modes.");
					return;
				}

				ThreadAccessors.A_CoordModePixel = val;
			}
		}

		/// <summary>
		/// The coordinate mode for positioning tooltips.
		/// </summary>
		/// <exception cref="ValueError">A <see cref="ValueError"/> exception is thrown if the value couldn't be converted to a <see cref="CoordModeType"/>.</exception>
		public static object A_CoordModeToolTip
		{
			get => CoordModeTypeToString(ThreadAccessors.A_CoordModeToolTip);

			set
			{
				var val = CoordModeType.Client;

				if (value is CoordModeType cmt)
					val = cmt;
				else if (Enum.TryParse(value.As(), out cmt))
					val = cmt;
				else
				{
					_ = Errors.ValueErrorOccurred($"{value} was not in the correct format for coordinate modes.");
					return;
				}

				ThreadAccessors.A_CoordModeToolTip = val;
			}
		}

		/// <summary>
		/// The type of mouse cursor currently being displayed. It will be one of the following words:<br/>
		/// AppStarting, Arrow, Cross, Help, IBeam, Icon, No, Size, SizeAll, SizeNESW, SizeNS, SizeNWSE, SizeWE, UpArrow, Wait, Unknown.<br/>
		/// The acronyms used with the size-type cursors are compass directions, e.g. NESW = NorthEast+SouthWest.<br/>
		/// The hand-shaped cursors (pointing and grabbing) are classified as Unknown.
		/// </summary>
		public static object A_Cursor
		{
			get => ThreadAccessors.A_Cursor;
			set => ThreadAccessors.A_Cursor = value.ToString();
		}

		/// <summary>
		/// See <see cref="A_MDay"/>.
		/// </summary>
		public static string A_DD => A_MDay;

		/// <summary>
		/// Current day of the week's 3-letter abbreviation in the current user's language, e.g. Sun.
		/// </summary>
		public static string A_DDD => DateTime.Now.ToString("ddd");

		/// <summary>
		/// Current day of the week's full name in the current user's language, e.g. Sunday.
		/// </summary>
		public static string A_DDDD => DateTime.Now.ToString("dddd");

		/// <summary>
		/// Sets the mouse speed that will be used if unspecified in <see cref="Click"/>.
		/// </summary>
		public static object A_DefaultMouseSpeed
		{
			get => ThreadAccessors.A_DefaultMouseSpeed;
			set => ThreadAccessors.A_DefaultMouseSpeed = value.ToLong();
		}

		/// <summary>
		/// The full path and name of the folder containing the current user's desktop files.
		/// </summary>
		// DoNotVerify for the same reason as A_MyDocuments: "" on a box with no ~/Desktop.
		public static string A_Desktop =>
			Environment.GetFolderPath(Environment.SpecialFolder.Desktop, Environment.SpecialFolderOption.DoNotVerify);

		/// <summary>
		/// The full path and name of the folder containing the all-users desktop files.
		/// </summary>
		public static string A_DesktopCommon => Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);

		/// <summary>
		/// Determines whether invisible text in a window is "seen" for the purpose of finding the window. This affects commands such as <see cref="WinExist"/> and <see cref="WinActivate"/>.
		/// </summary>
		public static object A_DetectHiddenText
		{
			get => ThreadAccessors.A_DetectHiddenText;

			set
			{
				var val = Options.OnOff(value);

				if (val != null)
				{
					var b = val.Value.Ab();
					ThreadAccessors.A_DetectHiddenText = b;
				}
			}
		}

		/// <summary>
		/// Determines whether invisible windows are "seen".
		/// </summary>
		public static object A_DetectHiddenWindows
		{
			get => ThreadAccessors.A_DetectHiddenWindows;

			set
			{
				var val = Options.OnOff(value);

				if (val != null)
				{
					var b = val.Value.Ab();
					ThreadAccessors.A_DetectHiddenWindows = b;
				}
			}
		}

		/// <summary>
		/// The ending character that was pressed by the user to trigger the most recent non-auto-replace hotstring. If no ending character was required (due to the * option), this variable will be blank.
		/// </summary>
		public static object A_EndChar
		{
			get;
			internal set;
		}

		/// <summary>
		/// Contains event information from various commands.
		/// </summary>
		public static object A_EventInfo
		{
			get => ThreadAccessors.A_EventInfo;
			set => ThreadAccessors.A_EventInfo = value;
		}

		/// <summary>
		/// The result from last Windows GetLastError() function call which is stored after certain
		/// function calls such as DllCall and file functions.
		/// </summary>
		public static object A_LastError
		{
			get => (long)ThreadAccessors.A_LastError;
			set
			{
				var val = value.ToInt();
				Marshal.SetLastSystemError(val);
				ThreadAccessors.A_LastError = val;
			}
		}

		/// <summary>
		/// The default file encoding setting.
		/// </summary>
		public static object A_FileEncoding
		{
			get => ThreadAccessors.A_FileEncoding;

			set
			{
				var val = Files.GetEncoding(value.ToString());
				ThreadAccessors.A_FileEncoding = val;
			}
		}

		/// <summary>
		/// The callback (function object) which represents the current HotIf criteria for the Hotkey and Hotstring functions, or blank if none.
		/// </summary>
		public static object A_HotIf => (Script.TheScript.Threads.CurrentThread.hotCriterion as KeysharpFunc) ?? (object)DefaultObject;


		/// <summary>
		/// The rate in milliseconds of hotkey activations beyond which a warning dialog will be displayed.
		/// </summary>
		public static object A_HotkeyInterval
		{
			get => Script.TheScript.AccessorData.hotkeyThrottleInterval;
			set => Script.TheScript.AccessorData.hotkeyThrottleInterval = value.ToLong();
		}

		/// <summary>
		/// How long after a hotkey is pressed that its modifier keys are assumed to still be held down.<br/>
		/// This is used by Send to determine whether to push the modifier keys back down after having temporarily released them.
		/// </summary>
		public static object A_HotkeyModifierTimeout
		{
			get => Script.TheScript.AccessorData.hotkeyModifierTimeout;
			set => Script.TheScript.AccessorData.hotkeyModifierTimeout = value.ToLong();
		}

		/// <summary>
		/// Current 2-digit hour (00-23) in 24-hour time (for example, 17 is 5pm).
		/// </summary>
		public static string A_Hour => DateTime.Now.ToString("HH");

		/// <summary>
		/// Blank unless a custom tray icon has been specified via Menu, tray, icon -- in which case it's the full path and name of the icon's file.
		/// </summary>
		public static string A_IconFile
		{
			get => Script.TheScript.AccessorData.iconFile;
			internal set => Script.TheScript.AccessorData.iconFile = value;
		}

		/// <summary>
		/// Contains true if the tray icon is currently hidden or false otherwise. The icon can be hidden via #NoTrayIcon or the Menu command.
		/// </summary>
		public static object A_IconHidden
		{
			get => Script.TheScript.AccessorData.iconHidden;

			set
			{
				var script = Script.TheScript;

				if (script.NoTrayIcon)
					return;

				var val = Options.OnOff(value);

				if (val.HasValue)
				{
					if (!val.Value && script.AccessorData.iconHidden)//Was true, switching to false, so show.
					{
						if (script.EnsureTrayIcon())
						{
							script.Tray.Icon = script.AccessorData.prevTrayIcon;
							script.AccessorData.prevTrayIcon = null;
							script.AccessorData.iconHidden = val.Value;
						}
					}
					else if (val.Value && !script.AccessorData.iconHidden)//Was false, switching to true, so hide.
					{
						if (script.Tray != null)
						{
							script.AccessorData.prevTrayIcon = script.Tray.Icon;
							script.Tray.Icon = null;
							script.AccessorData.iconHidden = val.Value;
						}
					}
				}
			}
		}

		/// <summary>
		/// Blank if A_IconFile is blank. Otherwise, it's the number of the icon in A_IconFile (typically 1) or a string name of an icon in a resource in a .NET DLL.
		/// </summary>
		public static object A_IconNumber
		{
			get => Script.TheScript.AccessorData.iconNumber;
			internal set => Script.TheScript.AccessorData.iconNumber = value;
		}

		/// <summary>
		/// The tray icon's tooltip, which defaults to the script name. It belongs to the script rather than to the
		/// icon, so it reads and writes with no icon on screen and an icon shown later picks it up.
		/// </summary>
		public static object A_IconTip
		{
			get => Script.TheScript.TrayTip;

			set => Script.TheScript.TrayTip = value.ToString();
		}

		/// <summary>
		/// The number of the current loop iteration.
		/// </summary>
		public static object A_Index
		{
			get
			{
				var s = Loops.LoopStack;
				return s.TryPeek(out var result) ? result.index : default;
			}
			set
			{
				var s = Loops.LoopStack;

				if (s.TryPeek(out var result))
					result.index = value.ToLong();
			}
		}

		/// <summary>
		/// The script's initial working directory, which is determined by how it was launched.<br/>
		/// For example, if it was run via shortcut -- such as on the Start Menu -- its initial working directory is determined by the "Start in" field within the shortcut's properties.
		/// </summary>
		public static string A_InitialWorkingDir => Script.TheScript.AccessorData.initialWorkingDir;


		/// <summary>
		/// Whether the script is running on a 64-bit operating system. Always true.
		/// </summary>
		public static bool A_Is64bitOS => true;

		/// <summary>
		/// True if the current user has administrator rights, false otherwise.
		/// </summary>
		public static bool A_IsAdmin
		{
			get
			{
#if LINUX
				return Xlib.geteuid() == 0;
#elif OSX
				return string.Equals(Environment.UserName, "root", StringComparison.Ordinal);
#elif WINDOWS
				using var id = WindowsIdentity.GetCurrent();
				return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
#else
#error Unsupported platform. Only WINDOWS, LINUX, and OSX are supported.
#endif
			}
		}

		/// <summary>
		/// True if the current executing assembly is a compiled script, false otherwise;
		/// </summary>
		public static bool A_IsCompiled
		{
			get
			{
				var processName = Path.GetFileName(
#if WINDOWS
					Application.ExecutablePath
#else
					Environment.ProcessPath ?? string.Empty
#endif
					).ToLowerInvariant();

				if (processName is "keysharp.dll" or "keysharp.exe"
					or "keysharp"
					or "keyview.dll" or "keyview.exe"
					or "keyview"
					or "testhost.exe" or "testhost.dll"
					or "testhost"
					or "dotnet" or "dotnet.exe")
					return false;

				if (ScriptExecutionState.Assembly != null && string.IsNullOrEmpty(ScriptExecutionState.Assembly.Location))
					return false;

				return true;
			}
		}

		/// <summary>
		/// 1 if the current thread is marked as critical, else 0.
		/// </summary>
		public static long A_IsCritical
		{
			get
			{
				var tv = Script.TheScript.Threads.CurrentThread;
				return tv.isCritical ? tv.configData.peekFrequency : 0L;
			}
		}

		/// <summary>
		/// True if the script's hotkeys and hotstrings are suspended, else false.
		/// </summary>
		public static bool A_IsSuspended => Script.TheScript.FlowData.suspended;

		/// <summary>
		/// True if the thread immediately underneath the current thread is paused, otherwise false.
		/// </summary>
		public static bool A_IsPaused => ThreadAccessors.A_IsPaused;


		/// <summary>
		/// Only for compatibility with AHK, C# programs are always unicode.
		/// </summary>
		public static bool A_IsUnicode => true;

		/// <summary>
		/// Returns either 0, 1, 2 or 3:
		///     0: No keyboard hook is installed.
		///     1: Only our keyboard hook is installed.
		///     2: Only another keyboard hook is installed.
		///     3: Ours and another keyboard hook are installed.
		/// </summary>
		public static long A_KeybdHookInstalled => Script.TheScript.HookThread is HookThread ht
		? (ht.HasKbdHook() ? 1L : 0L) | (ht.SystemHasAnotherKeybdHook() ? 2L : 0L)
		: 0L;

		/// <summary>
		/// The delay that will occur after each keystroke sent by <see cref="Send"/> and <see cref="ControlSend"/>.
		/// </summary>
		public static object A_KeyDelay
		{
			get => ThreadAccessors.A_KeyDelay;
			set => ThreadAccessors.A_KeyDelay = value.ToLong();
		}

		/// <summary>
		/// The delay that will occur in SendPlay mode after each keystroke sent by <see cref="Send"/> and <see cref="ControlSend"/>.
		/// </summary>
		public static object A_KeyDelayPlay
		{
			get => ThreadAccessors.A_KeyDelayPlay;
			set => ThreadAccessors.A_KeyDelayPlay = value.ToLong();
		}

		/// <summary>
		/// The delay between the press of a key and before its release, used with <see cref="A_KeyDelay"/>.
		/// </summary>
		public static object A_KeyDuration
		{
			get => ThreadAccessors.A_KeyDuration;
			set => ThreadAccessors.A_KeyDuration = value.ToLong();
		}

		/// <summary>
		/// The delay in SendPlay mode between the press of a key and before its release, used with <see cref="A_KeyDelayPlay"/>.
		/// </summary>
		public static object A_KeyDurationPlay
		{
			get => ThreadAccessors.A_KeyDurationPlay;
			set => ThreadAccessors.A_KeyDurationPlay = value.ToLong();
		}


		/// <summary>
		/// ListLines is never true.
		/// </summary>
		public static object A_ListLines
		{
			get => 0L;

			set { }
		}

		/// <summary>
		/// The current element of a loop.
		/// </summary>
		public static object A_LoopField
		{
			get
			{
				var s = Loops.LoopStack;

				if (s.Count == 0)
					return DefaultObject;

				foreach (var l in s)//Since loop is a stack, this goes in reverse order, which is what we want.
				{
					switch (l.type)
					{
						case LoopType.Parse:
							return l.result;

						case LoopType.Each:
							{
								if (!(l.result is object[] so))
									return DefaultObject;

								return so.Length > 0 ? so[1] : DefaultObject;
							}
					}
				}

				return DefaultObject;
			}
		}

		/// <summary>
		/// The attributes of the file currently retrieved.
		/// </summary>
		public static string A_LoopFileAttrib
		{
			get
			{
				if (A_LoopFileFullPath is string s && (File.Exists(s) || Directory.Exists(s)))
				{
					var val = "";
					var attr = File.GetAttributes(s);

					if (attr.HasFlag(FileAttributes.ReadOnly))
						val += "R";

					if (attr.HasFlag(FileAttributes.Archive))
						val += "A";

					if (attr.HasFlag(FileAttributes.System))
						val += "S";

					if (attr.HasFlag(FileAttributes.Hidden))
						val += "H";

					if (attr.HasFlag(FileAttributes.Normal))
						val += "N";

					if (attr.HasFlag(FileAttributes.Directory))
						val += "D";

					if (attr.HasFlag(FileAttributes.Offline))
						val += "O";

					if (attr.HasFlag(FileAttributes.Compressed))
						val += "C";

					if (attr.HasFlag(FileAttributes.Temporary))
						val += "T";

					return val;
				}

				return DefaultObject;
			}
		}

		/// <summary>
		/// The full path of the directory in which A_LoopFileName resides. However, if FilePattern contains a relative path rather than an absolute path, the path here will also be relative. A root directory will not contain a trailing backslash. For example: C:
		/// </summary>
		public static string A_LoopFileDir
		{
			get
			{
				var loop = Loops.GetDirLoop();
				return loop != null && loop.file is string s ? Path.GetDirectoryName(s) : null;
			}
		}

		/// <summary>
		/// The file's extension (e.g. TXT, DOC, or EXE). The period (.) is not included.
		/// </summary>
		public static string A_LoopFileExt
		{
			get
			{
				var file = Loops.GetDirLoopFilename();
				return file != null ? Path.GetExtension(file).TrimStart('.') : "";
			}
		}

		/// <summary>
		/// This is different than A_LoopFilePath in the following ways:
		/// 1) It always contains the absolute/complete path of the file even if FilePattern contains a relative path
		/// 2) Any short (8.3) folder names in FilePattern itself are converted to their long names
		/// 3) Characters in FilePattern are converted to uppercase or lowercase to match the case stored in the file system.
		/// This is useful for converting file names -- such as those passed into a script as command line parameters -- to their exact path names as shown by Explorer.
		/// </summary>
		public static string A_LoopFileFullPath
		{
			get
			{
				var loop = Loops.GetDirLoop();
				return loop != null && loop.file is string s ? Loops.GetExactPath(s) : "";//This gives exact case.
			}
		}

		/// <summary>
		/// This is just a synonym for A_LoopFileFullPath.
		/// </summary>
		public static string A_LoopFileLongPath => A_LoopFileFullPath;

		/// <summary>
		/// The name of the file or folder currently retrieved (without the path).
		/// </summary>
		public static string A_LoopFileName
		{
			get
			{
				var file = Loops.GetDirLoopFilename();
				return file != null ? Path.GetFileName(file) : "";
			}
		}

		/// <summary>
		/// The path and name of the file/folder currently retrieved. If FilePattern contains a relative path rather than an absolute path, the path here will also be relative.
		/// </summary>
		public static string A_LoopFilePath
		{
			get
			{
				var loop = Loops.GetDirLoop();

				if (loop != null && loop.file is string s)
				{
					var fullpath = Path.GetFullPath(s);
					var isrel = !Path.IsPathFullyQualified(loop.path);
					return isrel ? Path.GetRelativePath(A_WorkingDir as string, fullpath) : fullpath;
				}

				return DefaultObject;
			}
		}

		/// <summary>
		/// The 8.3 short name, or alternate name of the file. If the file doesn't have one (due to the long name being shorter than 8.3 or perhaps because short-name generation is disabled on an NTFS file system), A_LoopFileName will be retrieved instead.
		/// </summary>
		public static string A_LoopFileShortName => A_LoopFileShortPath is string s ? Path.GetFileName(s) : null;

		/// <summary>
		/// The 8.3 short path and name of the file/folder currently retrieved. For example: C:\MYDOCU~1\ADDRES~1.txt. However, if FilePattern contains a relative path rather than an absolute path, the path here will also be relative.
		/// </summary>
		public static string A_LoopFileShortPath
		{
			get
			{
				var loop = Loops.GetDirLoop();
				return loop != null && loop.file is string s ? Loops.GetShortPath(s) : "";
			}
		}

		/// <summary>
		/// The size in bytes of the file currently retrieved. Files larger than 4 gigabytes are also supported.
		/// </summary>
		public static long A_LoopFileSize
		{
			get
			{
				var file = Loops.GetDirLoopFilename();
				return file != null && File.Exists(file) ? new FileInfo(file).Length : 0L;
			}
		}

		/// <summary>
		/// The size in Kbytes of the file currently retrieved, rounded down to the nearest integer.
		/// </summary>
		public static long A_LoopFileSizeKB => A_LoopFileSize / 1024L;

		/// <summary>
		/// The size in Mbytes of the file currently retrieved, rounded down to the nearest integer.
		/// </summary>
		public static long A_LoopFileSizeMB => A_LoopFileSize / (1024L * 1024L);

		/// <summary>
		/// The time the file was last accessed. Format YYYYMMDDHH24MISS.
		/// </summary>
		public static string A_LoopFileTimeAccessed
		{
			get
			{
				var file = Loops.GetDirLoopFilename();
				return !string.IsNullOrEmpty(file) ? Conversions.ToYYYYMMDDHH24MISS(File.GetLastAccessTime(file)) : "";
			}
		}

		/// <summary>
		/// The time the file was created. Format YYYYMMDDHH24MISS.
		/// </summary>
		public static string A_LoopFileTimeCreated
		{
			get
			{
				var file = Loops.GetDirLoopFilename();
				return !string.IsNullOrEmpty(file) ? Conversions.ToYYYYMMDDHH24MISS(File.GetCreationTime(file)) : "";
			}
		}

		/// <summary>
		/// The time the file was last modified. Format YYYYMMDDHH24MISS.
		/// </summary>
		public static string A_LoopFileTimeModified
		{
			get
			{
				var file = Loops.GetDirLoopFilename();
				return !string.IsNullOrEmpty(file) ? Conversions.ToYYYYMMDDHH24MISS(File.GetLastWriteTime(file)) : "";
			}
		}


		/// <summary>
		/// The contents of the current line in a file read loop excluding the carriage return and linefeed (`r`n) that marks the end of the line.
		/// </summary>
		public static string A_LoopReadLine
		{
			get
			{
				var s = Loops.LoopStack;

				if (s.Count == 0)
					return DefaultObject;

				foreach (var l in s)
				{
					switch (l.type)
					{
						case LoopType.File:
							return l.line;
					}
				}

				return DefaultObject;
			}
		}

#if WINDOWS

		/// <summary>
		/// The name of the registry loop root key being accessed (HKEY_LOCAL_MACHINE, HKEY_USERS, HKEY_CURRENT_USER, HKEY_CLASSES_ROOT, or HKEY_CURRENT_CONFIG).<br/>
		/// For remote registry access, this value will not include the computer name.
		/// </summary>
		public static string A_LoopRegKey
		{
			get
			{
				var s = Loops.LoopStack;

				if (s.Count == 0)
					return DefaultObject;

				foreach (var l in s)//Since loop is a stack, this goes in reverse order, which is what we want.
				{
					switch (l.type)
					{
						case LoopType.Registry:
							return l.regKeyName;
					}
				}

				return DefaultObject;
			}
		}

		/// <summary>
		/// The name of the currently retrieved registry loop item, which can be either a value name or the name of a subkey.<br/>
		/// Value names displayed by Windows RegEdit as "(Default)" will be retrieved if a value has been assigned to them, but <see cref="A_LoopRegName"/> will be blank for them.
		/// </summary>
		public static string A_LoopRegName
		{
			get
			{
				var s = Loops.LoopStack;

				if (s.Count == 0)
					return DefaultObject;

				foreach (var l in s)
				{
					switch (l.type)
					{
						case LoopType.Registry:
							return l.regName != "(Default)" ? l.regName : "";
					}
				}

				return DefaultObject;
			}
		}

		/// <summary>
		/// The time the current registry loop subkey or any of its values was last modified. Format YYYYMMDDHH24MISS.<br/>
		/// This variable will be empty if the currently retrieved item is not a subkey (i.e. <see cref="A_LoopRegType" is not the word KEY).
		/// </summary>
		public static object A_LoopRegTimeModified
		{
			get
			{
				var s = Loops.LoopStack;

				if (s.Count == 0)
					return DefaultObject;

				foreach (var l in s)
				{
					switch (l.type)
					{
						case LoopType.Registry:
							return l.regDate;
					}
				}

				return DefaultObject;
			}
		}

		/// <summary>
		/// The type of the currently retrieved registry loop item, which is one of the following words:<br/>
		/// KEY (i.e. the currently retrieved item is a subkey not a value), REG_SZ, REG_EXPAND_SZ, REG_MULTI_SZ, REG_DWORD, REG_QWORD, REG_BINARY, REG_LINK, REG_RESOURCE_LIST, REG_FULL_RESOURCE_DESCRIPTOR, REG_RESOURCE_REQUIREMENTS_LIST, REG_DWORD_BIG_ENDIAN (probably rare on most Windows hardware).<br/>
		/// It will be empty if the currently retrieved item is of an unknown type.
		/// </summary>
		public static string A_LoopRegType
		{
			get
			{
				var s = Loops.LoopStack;

				if (s.Count == 0)
					return DefaultObject;

				foreach (var l in s)
				{
					switch (l.type)
					{
						case LoopType.Registry:
							return l.regType;
					}
				}

				return DefaultObject;
			}
		}

#endif

		/// <summary>
		/// The maximum number of hotkeys that can be pressed within the interval without triggering a warning dialog.
		/// </summary>
		public static object A_MaxHotkeysPerInterval
		{
			get => Script.TheScript.AccessorData.maxHotkeysPerInterval;
			set => Script.TheScript.AccessorData.maxHotkeysPerInterval = value.ToLong();
		}



		/// <summary>
		/// Current 2-digit day of the month (01-31).
		/// </summary>
		public static string A_MDay => DateTime.Now.ToString("dd");

		/// <summary>
		/// The key that is used to mask Win or Alt keyup events.
		/// </summary>
		public static object A_MenuMaskKey
		{
			get => Script.TheScript.AccessorData.menuMaskKey;
			set => Script.TheScript.AccessorData.menuMaskKey = value.ToString();
		}

		/// <summary>
		/// Current 2-digit minute (00-59).
		/// </summary>
		public static string A_Min => DateTime.Now.ToString("mm");

		/// <summary>
		/// Current 2-digit month (01-12). Synonymous with <see cref="A_Mon"/>.
		/// </summary>
		public static string A_MM => A_Mon;

		/// <summary>
		/// Current month's abbreviation in the current user's language, e.g. Jul.
		/// </summary>
		public static string A_MMM => DateTime.Now.ToString("MMM");

		/// <summary>
		/// Current month's full name in the current user's language, e.g. July.
		/// </summary>
		public static string A_MMMM => DateTime.Now.ToString("MMMM");

		/// <summary>
		/// Current 2-digit month (01-12).
		/// </summary>
		public static string A_Mon => DateTime.Now.ToString("MM");

		/// <summary>
		/// Sets the delay that will occur after each mouse movement or click.
		/// </summary>
		public static object A_MouseDelay
		{
			get => ThreadAccessors.A_MouseDelay;
			set => ThreadAccessors.A_MouseDelay = value.ToLong();
		}

		/// <summary>
		/// Sets the delay that will occur in SendPlay mode after each mouse movement or click.
		/// </summary>
		public static object A_MouseDelayPlay
		{
			get => ThreadAccessors.A_MouseDelayPlay;
			set => ThreadAccessors.A_MouseDelayPlay = value.ToLong();
		}

		/// <summary>
		/// Returns either 0, 1, 2 or 3:
		///     0: No mouse hook is installed.
		///     1: Only our mouse hook is installed.
		///     2: Only another mouse hook is installed.
		///     3: Ours and another mouse hook are installed.
		/// </summary>
		public static long A_MouseHookInstalled => Script.TheScript.HookThread is HookThread ht
		? (ht.HasMouseHook() ? 1L : 0L) | (ht.SystemHasAnotherMouseHook() ? 2L : 0L)
		: 0L;

		/// <summary>
		/// Current 3-digit millisecond (000-999).
		/// </summary>
		public static string A_MSec => DateTime.Now.ToString("fff");

		/// <summary>
		/// The full path and name of the current user's "My Documents" folder.
		/// </summary>
		// DoNotVerify because the default returns "" for a folder that does not exist yet (a box with no
		// ~/Documents), which silently drops the user tier from the library and #import searches.
		public static string A_MyDocuments =>
			Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments, Environment.SpecialFolderOption.DoNotVerify);


		/// <summary>
		/// The current local time in YYYYMMDDHH24MISS format.
		/// </summary>
		public static string A_Now => Conversions.ToYYYYMMDDHH24MISS(DateTime.Now);

		/// <summary>
		/// The current Coordinated Universal Time (UTC) in YYYYMMDDHH24MISS format.
		/// </summary>
		public static string A_NowUTC => Conversions.ToYYYYMMDDHH24MISS(DateTime.UtcNow);

		/// <summary>
		/// Keysharp's current platform name, matching the corresponding script preprocessor symbol.
		/// </summary>
#if WINDOWS
		public static string A_OSType => "WINDOWS";
#elif OSX
		public static string A_OSType => "OSX";
#elif LINUX
		public static string A_OSType => "LINUX";
#else
		public static string A_OSType => "UNKNOWN";
#endif

		/// <summary>
		/// The Operating System version, e.g. WIN_VISTA, WIN_2003, WIN_XP, WIN_2000, WIN_NT4, WIN_95, WIN_98, WIN_ME.
		/// </summary>
		public static string A_OSVersion => $"{Environment.OSVersion.Version.Major}.{Environment.OSVersion.Version.Minor}.{Environment.OSVersion.Version.Build}";

		/// <summary>
		/// The key name of the previously executed hotkey or hotstring.
		/// </summary>
		public static string A_PriorHotkey => Script.TheScript.priorHotkeyName;


		/// <summary>
		/// The last key typed.
		/// All input generated by the script is excluded.
		/// </summary>
		public static string A_PriorKey => Script.TheScript.HookThread is HookThread ht ? ht.keyHistory.PriorKey() : "";

		/// <summary>
		/// The full path and name of the Program Files directory (e.g. C:\Program Files).
		/// </summary>
		public static string A_ProgramFiles => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

		/// <summary>
		/// The full path and name of the Programs folder in the current user's Start Menu.
		/// </summary>
		public static string A_Programs => Environment.GetFolderPath(Environment.SpecialFolder.Programs);

		/// <summary>
		/// The full path and name of the Programs folder in the all-users Start Menu.
		/// </summary>
		public static string A_ProgramsCommon => Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);

		/// <summary>
		/// The size in bytes of a pointer, always 8.
		/// </summary>
		public static long A_PtrSize => 8L;

#if WINDOWS

		/// <summary>
		/// The current registry view, either 32 or 64.
		/// </summary>
		public static object A_RegView
		{
			get => ThreadAccessors.A_RegView;

			set
			{
				var val = value is string s && s.Equals("default", StringComparison.CurrentCultureIgnoreCase) ? 64L : value.Al() == 32L ? 32L : 64L;
				ThreadAccessors.A_RegView = val;
			}
		}

#endif

		/// <summary>
		/// Number of pixels per logical inch along the screen width. In a system with multiple display monitors,
		/// this value is the same for all monitors. On most systems this is 96; it depends on the system's text size (DPI) setting.
		/// See also the GUI's -DPIScale option.
		/// </summary>
		public static double A_ScreenDPI
		{
			get
			{
#if WINDOWS
				using (var graphics = Graphics.FromHwnd(0))//This will only get the DPI for the first screen.
				{
					var x = graphics.DpiX;
					//var y = graphics.DpiY;
					return x;
				}
#else
				try
				{
					return Forms.Screen.PrimaryScreen.RealDPI;
				}
				catch
				{
					return 96.0;
				}
#endif
			}
		}

		/// <summary>
		/// The height of the primary monitor in pixels.
		/// </summary>
		public static long A_ScreenHeight
			{
				get
				{
					try
					{
						return Monitor.GetPrimaryScreenSize().Height;
					}
					catch
					{
						return 0L;
				}
			}
		}

		/// <summary>
		/// The width of the primary monitor in pixels.
		/// </summary>
		public static long A_ScreenWidth
			{
				get
				{
					try
					{
						return Monitor.GetPrimaryScreenSize().Width;
					}
					catch
					{
						return 0L;
				}
			}
		}

		/// <summary>
		/// The directory the script is running in.
		/// This will be the location of the executable if it's a compiled Script.TheScript.
		/// </summary>
		public static string A_ScriptDir
		{
			get
			{
				var script = Script.TheScript;

				if (A_IsCompiled)
					return Path.GetDirectoryName(GetAssembly().Location);
				else if (script.scriptPath == "*")
					return A_WorkingDir as string;
				else
					return Path.GetDirectoryName(script.scriptPath);
			}
		}

		/// <summary>
		/// The full path of the script location.
		/// </summary>
		public static string A_ScriptFullPath => A_IsCompiled ? A_AhkPath : Script.TheScript.scriptPath;

		/// <summary>
		/// The unique ID (HWND/handle) of the script's hidden main window.
		/// </summary>
		public static long A_ScriptHwnd => Script.TheScript.MainWindowHandle.ToInt64();

		/// <summary>
		/// The file name of the script.
		/// </summary>
		public static string A_ScriptName => Script.TheScript?.scriptName;

		/// <summary>
		/// Current 2-digit second (00-59).
		/// </summary>
		public static string A_Sec => DateTime.Now.ToString("ss");

		/// <summary>
		/// The send level to use when sending keys.<br/>
		/// The range is 0-100.
		/// </summary>
		public static object A_SendLevel
		{
			get => ThreadAccessors.A_SendLevel;
			set => ThreadAccessors.A_SendLevel = Math.Clamp(value.ToLong(), 0L, 100L);
		}

		/// <summary>
		/// The send mode to use for sending keys.<br/>
		/// Possible values are Event, Input, Play, and InputThenPlay.
		/// </summary>
		public static object A_SendMode
		{
			get => ThreadAccessors.A_SendMode.ToString();

			set
			{
				if (Enum.TryParse<SendModes>(value.As(), out var val))
				{
					ThreadAccessors.A_SendMode = val;
				}
			}
		}

		/// <summary>
		/// This variable contains a single space character.
		/// </summary>
		public static string A_Space => " ";

		/// <summary>
		/// The full path and name of the current user's Start Menu folder.
		/// </summary>
		public static string A_StartMenu => Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);

		/// <summary>
		/// The full path and name of the all-users Start Menu folder.
		/// </summary>
		public static string A_StartMenuCommon => Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu);

		/// <summary>
		/// The full path and name of the Startup folder in the current user's Start Menu.
		/// </summary>
		public static string A_Startup => Environment.GetFolderPath(Environment.SpecialFolder.Startup);

		/// <summary>
		/// The full path and name of the Startup folder in the all-users Start Menu.
		/// </summary>
		public static string A_StartupCommon => Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);

		/// <summary>
		/// Whether to restore the state of CapsLock after a <see cref="Send"/>.
		/// </summary>
		public static object A_StoreCapsLockMode
		{
			get => ThreadAccessors.A_StoreCapsLockMode;

			set
			{
				var val = Options.OnOff(value);

				if (val != null)
				{
					ThreadAccessors.A_StoreCapsLockMode = val.Value;
				}
			}
		}

		/// <summary>
		/// This variable contains a single tab character.
		/// </summary>
		public static string A_Tab => "\t";

		/// <summary>
		/// The full path and name of the folder designated to hold temporary files.
		/// </summary>
		public static string A_Temp => Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);

		/// <summary>
		/// The name of the function currently being executed.
		/// <para>
		/// A lowered script body never reaches here: the Lowerer knows the name at the emission site and folds it
		/// to a literal. This serves the reflected callers that have no such site — chiefly a <c>#CSharp</c> member
		/// — so both fallbacks are approximations. The scope is the innermost <em>scope-publishing</em> ancestor,
		/// which is the executing function whenever one published a scope, and <c>NoInlining</c> is what makes
		/// <c>StackTrace(1, …)</c> skip this getter rather than the caller it is meant to name.
		/// </para>
		/// </summary>
		public static string A_ThisFunc
		{
			[MethodImpl(MethodImplOptions.NoInlining)]
			get
			{
				if (Script.executingUserFunc is { } scope)
					return scope.Name;

				foreach (var frame in new StackTrace(1, false).GetFrames())
				{
					if (frame.GetMethod() is not MethodInfo method
							|| method.DeclaringType?.Namespace != Keywords.MainNamespaceName)
						continue;

					return MethodPropertyHolder.GetOrAdd(method).QualifiedName;
				}

				return "";
			}
		}

		/// <summary>
		/// The key name of the most recently executed hotkey or hotstring.
		/// </summary>
		public static string A_ThisHotkey => Script.TheScript.thisHotkeyName;

		/// <summary>
		/// The number of milliseconds since the computer was rebooted.
		/// </summary>
		public static long A_TickCount => Environment.TickCount64;

		/// <summary>
		/// The number of milliseconds that have elapsed since the system last received keyboard, mouse, or other input. This is useful for determining whether the user is away. This variable will be blank unless the operating system is Windows 2000, XP, or beyond. Physical input from the user as well as artificial input generated by any program or script (such as the Send or MouseMove commands) will reset this value back to zero. Since this value tends to increase by increments of 10, do not check whether it is equal to another value. Instead, check whether it is greater or less than another value. For example: IfGreater, A_TimeIdle, 600000, MsgBox, The last keyboard or mouse activity was at least 10 minutes ago.
		/// </summary>
		public static long A_TimeIdle => TryGetIdleTime(out var milliseconds) ? milliseconds : 0L;

		/// <summary>
		/// If the keyboard hook is installed, this is the number of milliseconds that have elapsed since the system last received physical keyboard input.<br/>
		/// Otherwise, this variable is equivalent to <see cref="A_TimeIdle"/>.
		/// </summary>
		public static long A_TimeIdleKeyboard => Script.TheScript.HookThread is HookThread ht && ht.HasKbdHook()
		? (long)(DateTime.UtcNow - Script.TheScript.timeLastInputKeyboard).TotalMilliseconds
		: A_TimeIdle;

		/// <summary>
		/// If the mouse hook is installed, this is the number of milliseconds that have elapsed since the system last received physical mouse input.<br/>
		/// Otherwise, this variable is equivalent to <see cref="A_TimeIdle"/>.
		/// </summary>
		public static long A_TimeIdleMouse => Script.TheScript.HookThread is HookThread ht && ht.HasMouseHook()
		? (long)(DateTime.UtcNow - Script.TheScript.timeLastInputMouse).TotalMilliseconds
		: A_TimeIdle;

		/// <summary>
		/// Similar to above but ignores artificial keystrokes and/or mouse clicks whenever the corresponding hook (keyboard or mouse) is installed;<br/>
		/// that is, it responds only to physical events. (This prevents simulated keystrokes and mouse clicks from falsely indicating that a user is present.)<br/>
		/// If neither hook is installed, this variable is equivalent to <see cref="A_TimeIdle"/>.<br/>
		/// If only one hook is installed, only its type of physical input affects A_TimeIdlePhysical (the other/non-installed hook's input, both physical and artificial, has no effect).
		/// </summary>
		public static long A_TimeIdlePhysical => Script.TheScript.HookThread is HookThread ht && ht.HasEitherHook()
		? (long)(DateTime.UtcNow - Script.TheScript.timeLastInputPhysical).TotalMilliseconds
		: A_TimeIdle;

		/// <summary>
		/// Time in ms that have elapsed since <see cref="A_PriorHotkey"/> was pressed. It will be -1 whenever <see cref="A_PriorHotkey"/> is blank.
		/// </summary>
		public static long A_TimeSincePriorHotkey => string.IsNullOrEmpty(Script.TheScript.priorHotkeyName) ? -1L : (long)(DateTime.UtcNow - Script.TheScript.priorHotkeyStartTime).TotalMilliseconds;

		/// <summary>
		/// Time in ms that have elapsed since <see cref="A_ThisHotkey"/> was pressed. It will be -1 whenever <see cref="A_ThisHotkey"/> is blank.
		/// </summary>
		public static long A_TimeSinceThisHotkey => string.IsNullOrEmpty(Script.TheScript.thisHotkeyName) ? -1L : (long)(DateTime.UtcNow - Script.TheScript.thisHotkeyStartTime).TotalMilliseconds;

		/// <summary>
		/// The current mode set by <see cref="SetTitleMatchMode"/>: 1, 2, 3, or RegEx.
		/// </summary>
		public static object A_TitleMatchMode
		{
			get
			{
				var l = ThreadAccessors.A_TitleMatchMode;
				return l == 4L ? Keyword_RegEx : l;
			}
			set
			{
				var script = Script.TheScript;

				var val = value.ToString().ToLower() switch
				{
					"1" => 1L,
					"2" => 2L,
					"3" => 3L,
					Keyword_RegEx => 4L,
					_ => 2L
				};

				ThreadAccessors.A_TitleMatchMode = val;
			}
		}

		/// <summary>
		/// The current match speed (fast or slow) set by <see cref="SetTitleMatchMode"/>.
		/// </summary>
		public static object A_TitleMatchModeSpeed
		{
			get => ThreadAccessors.A_TitleMatchModeSpeed ? Keyword_Fast : Keyword_Slow;

			set
			{
				var val = false;
				var script = Script.TheScript;
				var str = value.ToString();
				switch (str)
				{
					case var x when x.Equals(Keyword_Fast, StringComparison.OrdinalIgnoreCase): val = true; break;
					case var x when x.Equals(Keyword_Slow, StringComparison.OrdinalIgnoreCase): val = false; break;
				}

				ThreadAccessors.A_TitleMatchModeSpeed = val;
			}
		}

		/// <summary>
		/// The current tray menu object.
		/// </summary>
		public static Menu A_TrayMenu
		{
			get
			{
				var script = Script.TheScript;
				_ = script.EnsureTrayMenu();
				return script.trayMenu;
			}
		}

		/// <summary>
		/// The logon name of the current user.
		/// </summary>
		public static string A_UserName => Environment.UserName;

		/// <summary>
		/// Current 1-digit day of the week (1-7). 1 is Sunday in all locales.
		/// </summary>
		public static long A_WDay => (int)DateTime.Now.DayOfWeek + 1;

		/// <summary>
		/// The current delay set by <see cref="SetWinDelay"/>.
		/// </summary>
		public static object A_WinDelay
		{
			get => ThreadAccessors.A_WinDelay;
			set => ThreadAccessors.A_WinDelay = value.ToLong();
		}

#if WINDOWS

		/// <summary>
		/// The Windows directory. For example: C:\Windows.
		/// </summary>
		public static string A_WinDir => Environment.GetFolderPath(Environment.SpecialFolder.Windows);

#endif

		/// <summary>
		/// The script's current working directory, which is where files will be accessed by default.
		/// </summary>
		public static object A_WorkingDir
		{
			get => Environment.CurrentDirectory;

			set
			{
				var str = value.ToString();

				if (Directory.Exists(str))
					Environment.CurrentDirectory = str;
			}
		}

		/// <summary>
		/// Current day of the year (1-366).
		/// </summary>
		public static long A_YDay => DateTime.Now.DayOfYear;

		/// <summary>
		/// Current 4-digit year (e.g. 2004).
		/// </summary>
		public static long A_Year => DateTime.Now.Year;

		/// <summary>
		/// Current year and week number (e.g. 200453) according to ISO 8601.
		/// </summary>
		public static string A_YWeek
		{
			get
			{
				var now = DateTime.Now;
				var cal = new GregorianCalendar(GregorianCalendarTypes.Localized);
				var week = cal.GetWeekOfYear(now, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
				return $"{now:yyyy}{week:D2}";
			}
		}

		/// <summary>
		/// See <see cref="A_Year"/>.
		/// </summary>
		public static long A_YYYY => A_Year;

		/// <summary>
		/// The numeric value for False, 0.
		/// </summary>
		public static long False => 0L;

		/// <summary>
		/// The numeric value for True, 1.
		/// </summary>
		public static long True => 1L;

		/// <summary>
		/// Internal helper to get/set whether the script's icon is frozen due to <see cref="TraySetIcon"/>.
		/// </summary>
		internal static object A_IconFrozen
		{
			get => Script.TheScript.AccessorData.iconFrozen ?? (Script.TheScript.AccessorData.iconFrozen = false).Value;
			set => Script.TheScript.AccessorData.iconFrozen = value.ParseBool();
		}


		internal static long ControlDelayDefault => Script.TheScript.AccessorData.threadConfigDataPrototype.controlDelay;
		internal static CoordModeType CoordModeCaretDefault => Script.TheScript.AccessorData.threadConfigDataPrototype.coordModeCaret;
		internal static CoordModeType CoordModeMenuDefault => Script.TheScript.AccessorData.threadConfigDataPrototype.coordModeMenu;
		internal static CoordModeType CoordModeMouseDefault => Script.TheScript.AccessorData.threadConfigDataPrototype.coordModeMouse;
		internal static CoordModeType CoordModePixelDefault => Script.TheScript.AccessorData.threadConfigDataPrototype.coordModePixel;
		internal static CoordModeType CoordModeToolTipDefault => Script.TheScript.AccessorData.threadConfigDataPrototype.coordModeToolTip;
		internal static long DefaultMouseSpeedDefault => Script.TheScript.AccessorData.threadConfigDataPrototype.defaultMouseSpeed;
		internal static bool DetectHiddenTextDefault => Script.TheScript.AccessorData.threadConfigDataPrototype.detectHiddenText;
		internal static bool DetectHiddenWindowsDefault => Script.TheScript.AccessorData.threadConfigDataPrototype.detectHiddenWindows;
		internal static Encoding FileEncodingDefault => Script.TheScript.AccessorData.threadConfigDataPrototype.fileEncoding;
		internal static long KeyDelayDefault => Script.TheScript.AccessorData.threadConfigDataPrototype.keyDelay;
		internal static long KeyDelayPlayDefault => Script.TheScript.AccessorData.threadConfigDataPrototype.keyDelayPlay;
		internal static long KeyDurationDefault => Script.TheScript.AccessorData.threadConfigDataPrototype.keyDuration;
		internal static long KeyDurationPlayDefault => Script.TheScript.AccessorData.threadConfigDataPrototype.keyDurationPlay;
		internal static long MouseDelayDefault => Script.TheScript.AccessorData.threadConfigDataPrototype.mouseDelay;
		internal static long MouseDelayPlayDefault => Script.TheScript.AccessorData.threadConfigDataPrototype.mouseDelayPlay;
		internal static long PeekFrequencyDefault => Script.TheScript.AccessorData.threadConfigDataPrototype.peekFrequency;
#if WINDOWS
		internal static long RegViewDefault => Script.TheScript.AccessorData.threadConfigDataPrototype.regView;
#endif
		internal static long SendLevelDefault => Script.TheScript.AccessorData.threadConfigDataPrototype.sendLevel;
		internal static SendModes SendModeDefault => Script.TheScript.AccessorData.threadConfigDataPrototype.sendMode;
		internal static bool StoreCapsLockModeDefault => Script.TheScript.AccessorData.threadConfigDataPrototype.storeCapsLockMode;
		internal static long TitleMatchModeDefault => Script.TheScript.AccessorData.threadConfigDataPrototype.titleMatchMode;
		internal static bool TitleMatchModeSpeedDefault => Script.TheScript.AccessorData.threadConfigDataPrototype.titleMatchModeSpeed;
		internal static long WinDelayDefault => Script.TheScript.AccessorData.threadConfigDataPrototype.winDelay;

		//if (A_IsCompiled != 0)//  return Path.GetFileName(GetAssembly().Location);//else if (scriptName == "*")//  return "*";//else//  return Path.GetFileName(scriptName);

		/// <summary>
		/// The assembly a script's own facts are read from.
		/// </summary>
		/// <remarks>
		/// The compiled script's own assembly whenever the process has one, which covers every normal run: an
		/// interpreted script, a .cks and a compiled exe all have a <see cref="Script.ProgramType"/>. Otherwise the
		/// process's entry assembly — for a script launched by the launcher that is Keysharp itself, which is the
		/// right answer to "what application is this?" when no script has claimed an identity of its own.
		/// </remarks>
		internal static Assembly GetAssembly() => Script.TheScript?.ProgramType?.Assembly ?? ScriptExecutionState.Assembly
			?? Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();

		/// <summary>
		/// Wrapper to get a string representation of the <see cref="CordModeType"/> enum because using
		/// ToString() is slow because it uses reflection.
		/// </summary>
		/// <param name="mode">The enum to return the string for.</param>
		/// <returns>The string representation of mode.</returns>
		internal static string CoordModeTypeToString(CoordModeType mode)
		{
			switch (mode)
			{
				case CoordModeType.Client:
					return "Client";

				case CoordModeType.Window:
					return "Window";

				case CoordModeType.Screen:
					return "Screen";

				default:
					return DefaultErrorString;
			}
		}


	}

	public partial class Ks
	{

		// Keysharp additions which have no AutoHotkey equivalent. They live here rather than in
		// Accessors so that a script's global namespace stays identical to AutoHotkey's; scripts
		// reach them with #Import Ks.
		/// <summary>
		/// The time in milliseconds to wait when reading the clipboard before a timeout is triggered.
		/// </summary>
		public static object A_ClipboardTimeout
		{
			get => Script.TheScript.AccessorData.clipboardTimeout;
			set => Script.TheScript.AccessorData.clipboardTimeout = value.Al();
		}

		/// <summary>
		/// The maximum time allowed for a #HotIf criterion evaluation.
		/// </summary>
		public static object A_HotIfTimeout
		{
			get => Script.TheScript.AccessorData.hotIfTimeout;
			set => Script.TheScript.AccessorData.hotIfTimeout = value.Al();
		}

		/// <summary>
		/// The default input level to use for subsequently created hotkeys and hotstrings.
		/// </summary>
		public static object A_InputLevel
		{
			get => Script.TheScript.AccessorData.inputLevel;
			set => Script.TheScript.AccessorData.inputLevel = Math.Clamp(value.ToLong(), 0L, 100L);
		}

		/// <summary>
		/// True if the script is persistent.
		/// </summary>
		public static object A_IsPersistent
		{
			get => TheScript.persistent;
			set => TheScript.persistent = ForceBool(value);
		}

		/// <summary>
		/// The current object key in a fpr-each loop.
		/// </summary>
		public static object A_LoopKey
		{
			get
			{
				var s = Loops.LoopStack;

				if (s.Count == 0)
					return DefaultObject;

				foreach (var l in s)
				{
					switch (l.type)
					{
						case LoopType.Each:
							{
								if (!(l.result is object[]))
									return DefaultObject;

								var pair = (object[])l.result;
								return pair[0];
							}
					}
				}

				return DefaultObject;
			}
		}

		/// <summary>
		/// The value specified by #MaxThreadsBuffer.
		/// Causes some or all hotkeys to buffer rather than ignore keypresses when their #MaxThreadsPerHotkey limit has been reached.
		/// </summary>
		public static object A_MaxThreadsBuffer
		{
			get => Script.TheScript.AccessorData.maxThreadsBuffer;
			set => Script.TheScript.AccessorData.maxThreadsBuffer = ForceBool(value);
		}

		/// <summary>
		/// The value specified by #MaxThreadsPerHotkey.
		/// The maximum number of simultaneous threads per hotkey or hotstring.
		/// </summary>
		public static object A_MaxThreadsPerHotkey
		{
			get => Script.TheScript.AccessorData.maxThreadsPerHotkey;
			set => Script.TheScript.AccessorData.maxThreadsPerHotkey = value.Aui();
		}

		/// <summary>
		/// The native newline string, i.e. "\n" on linux, "\r\n" on Windows.
		/// </summary>
		public static string A_NewLine => Environment.NewLine;

		/// <summary>
		/// The running thread's priority; assigning to it is equivalent to <c>Thread "Priority", n</c>.
		/// Every thread starts at 0. A thread is given a different priority only when it is launched
		/// (SetTimer, Hotkey, a hotstring definition, Menu.Add) or by the thread itself through this, so
		/// there is no process-wide default to change — writing this in the auto-execute section sets the
		/// priority of that thread alone, exactly as AutoHotkey specifies.
		/// </summary>
		public static object A_Priority
		{
			get => ThreadAccessors.A_Priority;
			set => ThreadAccessors.A_Priority = value.Al();
		}

		/// <summary>
		/// Gets or sets the current thread's message-check interval in milliseconds.
		/// </summary>
		public static object A_PeekFrequency
		{
			get => ThreadAccessors.A_PeekFrequency;
			set => ThreadAccessors.A_PeekFrequency = value.ToLong();
		}

		/// <summary>
		/// The current thread as a <see cref="KeysharpThread"/> object — the class scripts know as <c>Thread</c>:
		/// its ID, stack position, elapsed time, priority, interruptibility, the thread it interrupted, and
		/// cooperative termination.<br/>
		/// Import with <c>#import KS { A_Thread }</c>. Replaces the former <c>A_ThreadId</c>, which is now
		/// <c>A_Thread.Id</c>.
		/// </summary>
		public static object A_Thread => Script.TheScript.Threads.CurrentThreadObject;

		/// <summary>
		/// The real (operating system) thread the current pseudo-thread is running on, as a
		/// <see cref="Ks.RealThread"/> object. On the script's main thread this is the same object as
		/// <c>RealThread.Main</c>.<br/>
		/// Import with <c>#import KS { A_RealThread }</c>.
		/// </summary>
		public static object A_RealThread => Ks.RealThread.ForCurrentThread();

		/// <summary>
		/// The CPU architecture this script is running as: X64, ARM64, X86 or ARM, matching the preprocessor
		/// symbol of the same name (<c>#if ARM64</c>).<br/>
		/// This is the calling convention <see cref="DllCall"/> and <see cref="ComCall"/> have to obey, so it -
		/// not <see cref="Accessors.A_PtrSize"/> - is what interop code should branch on: A_PtrSize is 8 for
		/// both X64 and ARM64, so AutoHotkey's usual 32/64 test cannot tell them apart.<br/>
		/// Import with <c>#import Ks { A_ProcessArch }</c>.
		/// </summary>
		public static string A_ProcessArch => ArchName(RuntimeInformation.ProcessArchitecture);

		/// <summary>
		/// The CPU architecture of the operating system, using the same names as
		/// <see cref="A_ProcessArch"/>. It differs from A_ProcessArch only when the process runs emulated,
		/// e.g. an X64 build on ARM64 Windows, so prefer A_ProcessArch for anything interop-related.<br/>
		/// Import with <c>#import Ks { A_OSArch }</c>.
		/// </summary>
		public static string A_OSArch => ArchName(RuntimeInformation.OSArchitecture);

		// Kept in Keysharp's own casing rather than Architecture.ToString(), so the value is directly
		// comparable to the preprocessor symbol and to A_OSType's uppercase style.
		internal static string ArchName(Architecture arch) => arch switch
		{
			Architecture.X64 => "X64",
			Architecture.Arm64 => "ARM64",
			Architecture.X86 => "X86",
			Architecture.Arm => "ARM",
			_ => arch.ToString().ToUpperInvariant(),
		};

		/// <summary>
		/// The Keysharp build running the script, read from Keysharp.Core.dll. Distinct from
		/// <see cref="Accessors.A_AhkVersion"/>, which is the AutoHotkey version Keysharp targets.
		/// </summary>
		public static string A_KsVersion
		{
			get
			{
				var ver = typeof(Accessors).Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>();
				return ver != null ? ver.Version : "";
			}
		}
		/// <summary>
		/// Whether timers are allowed to operate in the current thread. Default: true.
		/// </summary>
		public static object A_AllowTimers
		{
			get
			{
				var script = Script.TheScript;
				return script?.Threads?.AllowTimers ?? true;
			}

			set
			{
				var script = Script.TheScript;

				if (script == null)
					return;

				var val = Options.OnOff(value);

				if (val.HasValue)
					script.Threads.AllowTimers = val.Value;
			}
		}

		/// <summary>
		/// The application-wide GUI theme.
		/// Possible values are Classic, System, and Dark.
		/// </summary>
		public static object A_GuiTheme
		{
			get => Script.TheScript.GetGuiTheme();
			set
			{
				if (!Script.TheScript.TrySetGuiTheme(value?.ToString()))
				{
					_ = Errors.ValueErrorOccurred($"Invalid gui theme {value}");
				}
			}
		}

		/// <summary>
		/// Iterates through all timers in existence and returns the number of them which are enabled.
		/// </summary>
		public static Map A_Timers
		{
			get
			{
				var timerData = new List<object>();
				foreach (var timer in Script.TheScript.FlowData.timers.GetSnapshot())
				{
					if (timer.Callback is KeysharpFunc func)
					{
						timerData.AddRange(func, timer.Enabled);
					}
				}

				return new Map(timerData.ToArray());
			}
		}

		/// <summary>
		/// The default case sensitivity of hotstrings.
		/// </summary>
		public static bool A_DefaultHotstringCaseSensitive => Script.TheScript.HotstringManager.hsCaseSensitive;

		/// <summary>
		/// The default case conformity of hotstrings.
		/// </summary>
		public static bool A_DefaultHotstringConformToCase => Script.TheScript.HotstringManager.hsConformToCase;

		/// <summary>
		/// The default inside word detection of hotstrings.
		/// </summary>
		public static bool A_DefaultHotstringDetectWhenInsideWord => Script.TheScript.HotstringManager.hsDetectWhenInsideWord;

		/// <summary>
		/// The default backspacing of hotstrings.
		/// </summary>
		public static bool A_DefaultHotstringDoBackspace => Script.TheScript.HotstringManager.hsDoBackspace;

		/// <summary>
		/// The default recognizer reset mode of hotstrings.
		/// </summary>
		public static bool A_DefaultHotstringDoReset => Script.TheScript.HotstringManager.hsDoReset;

		/// <summary>
		/// The default end char mode of hotstrings.
		/// </summary>
		public static bool A_DefaultHotstringEndCharRequired => Script.TheScript.HotstringManager.hsEndCharRequired;

		/// <summary>
		/// The default end chars of hotstrings.
		/// </summary>
		public static string A_DefaultHotstringEndChars => Script.TheScript.HotstringManager.defEndChars;

		/// <summary>
		/// The default end chars of hotstrings.
		/// </summary>
		public static long A_DefaultHotstringKeyDelay => Script.TheScript.HotstringManager.hsKeyDelay;

		/// <summary>
		/// Whether mouse clicks reset the hotstring recognizer.
		/// </summary>
		public static object A_DefaultHotstringNoMouse => !Script.TheScript.HotstringManager.hsResetUponMouseClick;

		/// <summary>
		/// The default end char omission mode of hotstrings.
		/// </summary>
		public static bool A_DefaultHotstringOmitEndChar => Script.TheScript.HotstringManager.hsOmitEndChar;

		/// <summary>
		/// The default priority of hotstrings.
		/// </summary>
		public static long A_DefaultHotstringPriority => Script.TheScript.HotstringManager.hsPriority;

		/// <summary>
		/// The default send mode of hotstrings.
		/// </summary>
		public static string A_DefaultHotstringSendMode
		{
			get
			{
				switch (Script.TheScript.HotstringManager.hsSendMode)
				{
					case SendModes.Event:
						return "Event";

					case SendModes.Input:
						return "Input";

					case SendModes.Play:
						return "Play";

					case SendModes.InputThenPlay:
						return "InputThenPlay";

					case SendModes.Invalid:
						return "Invalid";

					default:
						return DefaultErrorString;
				}
			}
		}

		/// <summary>
		/// The default send raw mode of hotstrings.
		/// </summary>
		public static string A_DefaultHotstringSendRaw
		{
			get
			{
				switch (Script.TheScript.HotstringManager.hsSendRaw)
				{
					case SendRawModes.NotRaw:
						return "NotRaw";

					case SendRawModes.Raw:
						return "Raw";

					case SendRawModes.RawText:
						return "RawText";

					default:
						return DefaultErrorString;
				}
			}
		}

		/// <summary>
		/// The native directory separator string, i.e. "/" on linux, "\" on Windows.
		/// </summary>
		public static string A_DirSeparator => Path.DirectorySeparatorChar.ToString();

		/// <summary>
		/// The full path of Keysharp.Core.dll, the assembly holding the engine and its built-in library.
		/// </summary>
		public static string A_KsCorePath => Assembly.GetAssembly(typeof(Accessors)).Location;

#if WINDOWS
		/// <summary>
		/// The name of the registry loop value being accessed.<br/>
		/// For remote registry access, this value will not include the computer name.
		/// </summary>
		public static object A_LoopRegValue
		{
			get
			{
				var s = Loops.LoopStack;

				if (s.Count == 0)
					return DefaultObject;

				foreach (var l in s)
				{
					switch (l.type)
					{
						case LoopType.Registry:
							return l.regVal;
					}
				}

				return DefaultObject;
			}
		}
#endif
		/// <summary>
		/// The maximum simultaneously running threads allowed in a script.
		/// </summary>
		public static object A_MaxThreads => (long)Script.TheScript.MaxThreadsTotal;

		/// <summary>
		/// The value specified by #NoTrayIcon.
		/// Disables the showing of a tray icon.
		/// </summary>
		public static bool A_NoTrayIcon => Script.TheScript.NoTrayIcon;

		/// <summary>
		/// The current local time in YYYYMMDDHH24MISS.fff format.
		/// </summary>
		public static string A_NowMs => Conversions.ToYYYYMMDDHH24MISSFFF(DateTime.Now);

		/// <summary>
		/// The current Coordinated Universal Time (UTC) in YYYYMMDDHH24MISS.fff format.
		/// </summary>
		public static string A_NowUTCMs => Conversions.ToYYYYMMDDHH24MISSFFF(DateTime.UtcNow);

		/// <summary>
		/// Whether the script is exempt from being able to be suspended.
		/// </summary>
		public static object A_SuspendExempt
		{
			get => Script.TheScript.HotstringManager.hsSuspendExempt;
			set => Script.TheScript.HotstringManager.hsSuspendExempt = ForceBool(value);
		}

		/// <summary>
		/// The value specified by #UseHook.
		/// </summary>
		public static bool A_UseHook => Script.TheScript.ForceKeybdHook;

		/// <summary>
		/// Whether #WinActivateForce was specified.
		/// </summary>
		public static bool A_WinActivateForce => Script.TheScript.WinActivateForce;
	}

	/// <summary>
	/// Thread specific variables.
	/// </summary>
}
