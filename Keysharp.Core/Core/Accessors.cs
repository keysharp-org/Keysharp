namespace Keysharp.Core
{
	internal class AccessorData
	{
		internal long hotkeyModifierTimeout = 50L;
		internal long hotkeyThrottleInterval = 2000L;
		internal long maxHotkeysPerInterval = 2000L;
		internal readonly string initialWorkingDir = Environment.CurrentDirectory;
		internal bool allowMainWindow = true;
		internal bool? iconFrozen;
		internal bool iconHidden;
		internal long inputLevel;
		internal string menuMaskKey = "";
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
		/// <summary>
		/// The version of the assembly that was used to compile the script that is currently running.
		/// This queries the AssemblyBuildVersionAttribute of the generated code, which is automatically added by the script compiler
		/// to match the version of the program the script compiler is being run from.
		/// This allows for a consistent value whether compiling and running a script, or running a compilex exe.
		/// </summary>
		public static string A_AhkBuildVersion
		{
			get
			{
				var ver = GetAssembly().GetCustomAttribute<AssemblyBuildVersionAttribute>();
				return ver != null ? ver.Version : "";
			}
		}

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
		/// The version of Keysharp.Core.dll
		/// </summary>
		public static string A_AhkVersion
		{
			get
			{
				var ver = typeof(Accessors).Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>();
				return ver != null ? ver.Version : "";
			}
		}

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
		/// For example on Windows: C:\Documents and Settings\Username\Application Data<br/>
		/// Linux: ~/
		/// </summary>
		public static string A_AppData =>
#if WINDOWS
		Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

#else//On linux, ApplicationData maps to home/.config which is a file, not a folder. So just map it to home instead.
		Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
#endif

		/// <summary>
		/// The full path and name of the folder containing the all-users application-specific data.
		/// </summary>
		public static string A_AppDataCommon => Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

		/// <summary>
		/// An <see cref="Array"/> containing the command line arguments used to run the script.
		/// </summary>
		public static Array A_Args { get; internal set; } = [];

		/// <summary>
		/// The company of the assembly that is currently executing, specified with the #ASSEMBLYCOMPANY directive.
		/// </summary>
		public static string A_AsmCompany
		{
			get
			{
				var ver = GetAssembly().GetCustomAttribute<AssemblyCompanyAttribute>();
				return ver != null ? ver.Company : DefaultObject;
			}
		}

		/// <summary>
		/// The configuration of the assembly that is currently executing, specified with the #ASSEMBLYCONFIGURATION directive.
		/// </summary>
		public static string A_AsmConfiguration
		{
			get
			{
				var ver = GetAssembly().GetCustomAttribute<AssemblyConfigurationAttribute>();
				return ver != null ? ver.Configuration : DefaultObject;
			}
		}

		/// <summary>
		/// The copyright of the assembly that is currently executing, specified with the #ASSEMBLYCOPYRIGHT directive.
		/// </summary>
		public static string A_AsmCopyright
		{
			get
			{
				var ver = GetAssembly().GetCustomAttribute<AssemblyCopyrightAttribute>();
				return ver != null ? ver.Copyright : DefaultObject;
			}
		}

		/// <summary>
		/// The description of the assembly that is currently executing, specified with the #ASSEMBLYDESCRIPTION directive.
		/// </summary>
		public static string A_AsmDescription
		{
			get
			{
				var ver = GetAssembly().GetCustomAttribute<AssemblyDescriptionAttribute>();
				return ver != null ? ver.Description : DefaultObject;
			}
		}

		/// <summary>
		/// The product of the assembly that is currently executing, specified with the #ASSEMBLYPRODUCT directive.
		/// </summary>
		public static string A_AsmProduct
		{
			get
			{
				var ver = GetAssembly().GetCustomAttribute<AssemblyProductAttribute>();
				return ver != null ? ver.Product : DefaultObject;
			}
		}

		/// <summary>
		/// The title of the assembly that is currently executing, specified with the #ASSEMBLYTITLE directive.
		/// </summary>
		public static string A_AsmTitle
		{
			get
			{
				var ver = GetAssembly().GetCustomAttribute<AssemblyTitleAttribute>();
				return ver != null ? ver.Title : DefaultObject;
			}
		}

		/// <summary>
		/// The trademark of the assembly that is currently executing, specified with the #ASSEMBLYTRADEMARK directive.
		/// </summary>
		public static string A_AsmTrademark
		{
			get
			{
				var ver = GetAssembly().GetCustomAttribute<AssemblyTrademarkAttribute>();
				return ver != null ? ver.Trademark : "";
			}
		}

		/// <summary>
		/// The version of the assembly that is currently executing, specified with the #ASSEMBLVERSION directive.
		/// </summary>
		public static string A_AsmVersion
		{
			get
			{
				var ver = GetAssembly().GetCustomAttribute<AssemblyFileVersionAttribute>();//For some reason it likes AssemblyFileVersionAttribute better than AssemblyVersionAttribute.
				return ver != null ? ver.Version : DefaultObject;
			}
		}

		/// <summary>
		/// The data on the clipboard.
		/// </summary>
		public static object A_Clipboard
		{
			get
			{
				//Even if we're on an STA thread, this can sometimes fail. So it's best to always
				//make sure it's on the main thread.
				var act = () =>
				{
#if WINDOWS
					if (WindowsAPI.OpenClipboard(A_ClipboardTimeout.Al()))//Will need a cross platform version of this.//TODO
					{
						_ = WindowsAPI.CloseClipboard();//Need to close it for it to work

						if (Clipboard.GetData(DataFormats.Text) is string text)
							return text;

						if (Clipboard.GetData(DataFormats.Html) is string html)
							return html;

						if (Clipboard.GetData(DataFormats.Rtf) is string rtf)
							return rtf;

						if (Clipboard.GetData(DataFormats.SymbolicLink) is string sym)
							return sym;

						if (Clipboard.GetData(DataFormats.UnicodeText) is string uni)
							return uni;

						if (Clipboard.GetData(DataFormats.OemText) is string oem)
							return oem;

						if (Clipboard.GetData(DataFormats.CommaSeparatedValue) is string csv)
							return csv;

						if (Clipboard.GetData(DataFormats.FileDrop) is string[] files)
							return string.Join(DefaultNewLine, files);
					}
					return DefaultObject;
#else
					return Clipboard.Instance.Text ?? DefaultObject;
#endif
				};
				var ret = "";
				Script.TheScript.mainWindow.CheckedInvoke(() => ret = act(), true);
				return ret;
			}
			set
			{
				Script.TheScript.mainWindow.CheckedInvoke(() =>
				{
#if !WINDOWS

					if (value == null || (value is string s && s?.Length == 0))
					{
						//Clipboard.Clear();//For some reason this doesn't work on linux. Bug reported here: https://github.com/DanielVanNoord/System.Windows.Forms/issues/17
						Clipboard.Instance.Clear();
					}
					else if (value is ClipboardAll arr)
						Env.RestoreClipboardAll(arr, 0L);
					else
						Clipboard.Instance.Text = value.ToString();

#else

					if (WindowsAPI.OpenClipboard(A_ClipboardTimeout.Al()))
					{
						_ = WindowsAPI.CloseClipboard();//Need to close it for it to work

						if (value == null || (value is string s && s?.Length == 0))
							Clipboard.Clear();
						else if (value is ClipboardAll arr)
							Env.RestoreClipboardAll(arr, (long)arr.Size);
						else
							Clipboard.SetDataObject(value.ToString(), true);
					}

#endif
				}, true);
			}
		}

		/// <summary>
		/// The time in milliseconds to wait when reading the clipboard before a timeout is triggered.
		/// </summary>
		public static object A_ClipboardTimeout { get; set; } = 1000L;

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
			set => ThreadAccessors.A_ControlDelay = value.Al();
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
		public static string A_Cursor
		{
			get
			{
#if WINDOWS
				return Cursor.Current is Cursor cur
					? cur.ToString().Trim(BothBrackets).Split(' ', StringSplitOptions.RemoveEmptyEntries)[1].Replace("Cursor", "")
					: "Default---";
#else
				return "Default---";
#endif
			}
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
			set => ThreadAccessors.A_DefaultMouseSpeed = value.Al();
		}

		/// <summary>
		/// The full path and name of the folder containing the current user's desktop files.
		/// </summary>
		public static string A_Desktop => Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

		/// <summary>
		/// The full path and name of the folder containing the all-users desktop files.
		/// </summary>
		public static string A_DesktopCommon => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

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
		/// Represents the natural logarithmic base, specified by the constant, e.
		/// </summary>
		public static double A_E => Math.E;

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
				var val = value.Ai();
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
		/// The timeout used for checking if a window exists during a #HotIf check.
		/// </summary>
		public static object A_HotIfTimeout { get; set; } = 1000L;

		/// <summary>
		/// The rate in milliseconds of hotkey activations beyond which a warning dialog will be displayed.
		/// </summary>
		public static object A_HotkeyInterval
		{
			get => Script.TheScript.AccessorData.hotkeyThrottleInterval;
			set => Script.TheScript.AccessorData.hotkeyThrottleInterval = value.Al();
		}

		/// <summary>
		/// How long after a hotkey is pressed that its modifier keys are assumed to still be held down.<br/>
		/// This is used by Send to determine whether to push the modifier keys back down after having temporarily released them.
		/// </summary>
		public static object A_HotkeyModifierTimeout
		{
			get => Script.TheScript.AccessorData.hotkeyModifierTimeout;
			set => Script.TheScript.AccessorData.hotkeyModifierTimeout = value.Al();
		}

		/// <summary>
		/// Current 2-digit hour (00-23) in 24-hour time (for example, 17 is 5pm).
		/// </summary>
		public static string A_Hour => DateTime.Now.ToString("HH");

		/// <summary>
		/// Blank unless a custom tray icon has been specified via Menu, tray, icon -- in which case it's the full path and name of the icon's file.
		/// </summary>
		public static string A_IconFile { get; internal set; } = "";

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
						if (script.Tray != null)
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
		public static object A_IconNumber { get; internal set; } = 1L;

		/// <summary>
		/// Blank unless a custom tooltip for the tray icon has been specified via Menu, Tray, Tip -- in which case it's the text of the tip.
		/// </summary>
		public static object A_IconTip
		{
			get => Script.TheScript.Tray != null ? Script.TheScript.Tray.Text : "";

			set
			{
				var script = Script.TheScript;

				if (script.Tray != null)
					script.Tray.Text = value.ToString();
			}
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
					result.index = value.Al();
			}
		}

		/// <summary>
		/// The script's initial working directory, which is determined by how it was launched.<br/>
		/// For example, if it was run via shortcut -- such as on the Start Menu -- its initial working directory is determined by the "Start in" field within the shortcut's properties.
		/// </summary>
		public static string A_InitialWorkingDir => Script.TheScript.AccessorData.initialWorkingDir;

		/// <summary>
		/// The default input level to use for subsequently created hotkeys and hotstrings.
		/// </summary>
		public static object A_InputLevel
		{
			get => Script.TheScript.AccessorData.inputLevel;
			set => Script.TheScript.AccessorData.inputLevel = Math.Clamp(value.Al(), 0L, 100L);
		}

		/// <summary>
		/// The IP addresses of the network adapters in the computer.
		/// </summary>
		public static Array A_IPAddress
		{
			get
			{
				var addr = Dns.GetHostEntry(Dns.GetHostName()).AddressList;
				var ips = new Array();

				for (var i = 0; i < addr.Length; i++)
					if (addr[i].AddressFamily == AddressFamily.InterNetwork)
						_ = ips.Add(addr[i].ToString());

				return ips;
			}
		}

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
#elif WINDOWS
				using var id = WindowsIdentity.GetCurrent();
				return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
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
				var path = Path.GetFileName(
#if WINDOWS
					Application.ExecutablePath
#else
					Environment.ProcessPath ?? string.Empty
#endif
					).ToLowerInvariant();
				return path != "keysharp.dll" && path != "keysharp.exe" && path != "testhost.exe"
					   && path != "keysharp" && path != "testhost" && path != "testhost.dll";
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
		/// True if the script is persistent.
		/// </summary>
		public static object A_IsPersistent
		{
			get => TheScript.persistent;
			set => TheScript.persistent = ForceBool(value);
		}

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
			set => ThreadAccessors.A_KeyDelay = value.Al();
		}

		/// <summary>
		/// The delay that will occur in SendPlay mode after each keystroke sent by <see cref="Send"/> and <see cref="ControlSend"/>.
		/// </summary>
		public static object A_KeyDelayPlay
		{
			get => ThreadAccessors.A_KeyDelayPlay;
			set => ThreadAccessors.A_KeyDelayPlay = value.Al();
		}

		/// <summary>
		/// The delay between the press of a key and before its release, used with <see cref="A_KeyDelay"/>.
		/// </summary>
		public static object A_KeyDuration
		{
			get => ThreadAccessors.A_KeyDuration;
			set => ThreadAccessors.A_KeyDuration = value.Al();
		}

		/// <summary>
		/// The delay in SendPlay mode between the press of a key and before its release, used with <see cref="A_KeyDelayPlay"/>.
		/// </summary>
		public static object A_KeyDurationPlay
		{
			get => ThreadAccessors.A_KeyDurationPlay;
			set => ThreadAccessors.A_KeyDurationPlay = value.Al();
		}

		/// <summary>
		/// <see cref="A_AhkPath"/>.
		/// </summary>
		public static string A_KeysharpPath => A_AhkPath;

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
			set => Script.TheScript.AccessorData.maxHotkeysPerInterval = value.Al();
		}

		/// <summary>
		/// The value specified by #MaxThreadsBuffer.
		/// Causes some or all hotkeys to buffer rather than ignore keypresses when their #MaxThreadsPerHotkey limit has been reached.
		/// </summary>
		public static object A_MaxThreadsBuffer { get; set; }

		/// <summary>
		/// The value specified by #MaxThreadsPerHotkey.
		/// The maximum number of simultaneous threads per hotkey or hotstring.
		/// </summary>
		public static object A_MaxThreadsPerHotkey { get; set; } = 1L;

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
			set => ThreadAccessors.A_MouseDelay = value.Al();
		}

		/// <summary>
		/// Sets the delay that will occur in SendPlay mode after each mouse movement or click.
		/// </summary>
		public static object A_MouseDelayPlay
		{
			get => ThreadAccessors.A_MouseDelayPlay;
			set => ThreadAccessors.A_MouseDelayPlay = value.Al();
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
		/// Need a cross platform way to do this.//TODO
		/// </summary>
		public static string A_MyDocuments => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

		/// <summary>
		/// The native newline string, i.e. "\n" on linux, "\r\n" on Windows.
		/// </summary>
		public static string A_NewLine => Environment.NewLine;

		/// <summary>
		/// The current local time in YYYYMMDDHH24MISS format.
		/// </summary>
		public static string A_Now => Conversions.ToYYYYMMDDHH24MISS(DateTime.Now);

		/// <summary>
		/// The current Coordinated Universal Time (UTC) in YYYYMMDDHH24MISS format.
		/// </summary>
		public static string A_NowUTC => Conversions.ToYYYYMMDDHH24MISS(DateTime.UtcNow);

		/// <summary>
		/// The type of Operating System being run, e.g. WIN32_WINDOWS for Windows 95/98/ME or WIN32_NT for Windows NT4/2000/XP/2003/Vista.
		/// </summary>
		public static string A_OSType => Conversions.ToOSType(Environment.OSVersion.Platform);

		/// <summary>
		/// The Operating System version, e.g. WIN_VISTA, WIN_2003, WIN_XP, WIN_2000, WIN_NT4, WIN_95, WIN_98, WIN_ME.
		/// </summary>
		public static string A_OSVersion => $"{Environment.OSVersion.Version.Major}.{Environment.OSVersion.Version.Minor}.{Environment.OSVersion.Version.Build}";

		/// <summary>
		/// Thread peek frequency in milliseconds.
		/// Unused because Keysharp is compiled and not interpreted.
		/// </summary>
		public static object A_PeekFrequency
		{
			get => ThreadAccessors.A_PeekFrequency;
			set => ThreadAccessors.A_PeekFrequency = value.Al();
		}

		/// <summary>
		/// Represents the ratio of the circumference of a circle to its diameter, specified by the constant, π.
		/// </summary>
		public static double A_PI => Math.PI;

		/// <summary>
		/// The key name of the previously executed hotkey or hotstring.
		/// </summary>
		public static string A_PriorHotkey => Script.TheScript.priorHotkeyName;

		/// <summary>
		/// The default priority to use for each thread.
		/// </summary>
		public static object A_Priority { get; set; } = 0L;

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
		public static string A_ProgramsCommon => Environment.GetFolderPath(Environment.SpecialFolder.Startup);

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
		public static long A_ScreenHeight => Forms.Screen.PrimaryScreen.Bounds.Height.Al();

		/// <summary>
		/// The width of the primary monitor in pixels.
		/// </summary>
		public static long A_ScreenWidth => Forms.Screen.PrimaryScreen.Bounds.Width.Al();

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
				else if (script.scriptName == "*")
					return A_WorkingDir as string;
				else
					return Path.GetDirectoryName(script.scriptName);
			}
		}

		/// <summary>
		/// The full path of the script location.
		/// </summary>
		public static string A_ScriptFullPath => A_IsCompiled ? A_AhkPath : Script.TheScript.scriptName;

		/// <summary>
		/// The unique ID (HWND/handle) of the script's hidden main window.
		/// </summary>
		public static long A_ScriptHwnd => Script.TheScript.MainWindowHandle.ToInt64();

		/// <summary>
		/// The file name of the script.
		/// </summary>
		public static string A_ScriptName => Path.GetFileName(Script.TheScript.scriptName);

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
			set => ThreadAccessors.A_SendLevel = Math.Clamp(value.Al(), 0L, 100L);
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
		public static string A_StartMenuCommon => Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);

		/// <summary>
		/// The full path and name of the Startup folder in the current user's Start Menu.
		/// </summary>
		public static string A_Startup => Environment.GetFolderPath(Environment.SpecialFolder.Startup);

		/// <summary>
		/// The full path and name of the Startup folder in the all-users Start Menu.
		/// </summary>
		public static string A_StartupCommon => Environment.GetFolderPath(Environment.SpecialFolder.Startup);

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
		/// Temporary file name.
		/// </summary>
		public static string A_TempFile => Path.GetTempFileName();

		/// <summary>
		/// The name of the function currently being executed.
		/// </summary>
		public static string A_ThisFunc => new StackFrame(1).GetMethod().Name;

		/// <summary>
		/// The key name of the most recently executed hotkey or hotstring.
		/// </summary>
		public static string A_ThisHotkey => Script.TheScript.thisHotkeyName;

		/// <summary>
		/// The name of the menu from which A_ThisMenuItem was selected.
		/// </summary>
		public static string A_ThisMenu => null;

		/// <summary>
		/// The name of the most recently selected custom menu item (blank if none).
		/// </summary>
		public static string A_ThisMenuItem => null;

		/// <summary>
		/// A number indicating the current position of A_ThisMenuItem within A_ThisMenu. The first item in the menu is 1, the second is 2, and so on. Menu separator lines are counted. This variable is blank if A_ThisMenuItem is blank or no longer exists within A_ThisMenu. It is also blank if A_ThisMenu itself no longer exists.
		/// </summary>
		public static string A_ThisMenuItemPos => null;

		/// <summary>
		/// The number of milliseconds since the computer was rebooted.
		/// </summary>
		public static long A_TickCount => Environment.TickCount64;

		/// <summary>
		/// The number of milliseconds that have elapsed since the system last received keyboard, mouse, or other input. This is useful for determining whether the user is away. This variable will be blank unless the operating system is Windows 2000, XP, or beyond. Physical input from the user as well as artificial input generated by any program or script (such as the Send or MouseMove commands) will reset this value back to zero. Since this value tends to increase by increments of 10, do not check whether it is equal to another value. Instead, check whether it is greater or less than another value. For example: IfGreater, A_TimeIdle, 600000, MsgBox, The last keyboard or mouse activity was at least 10 minutes ago.
		/// </summary>
		public static long A_TimeIdle
		{
			get
			{
#if WINDOWS
				var lii = LASTINPUTINFO.Default;
				return WindowsAPI.GetLastInputInfo(ref lii) ? Environment.TickCount - lii.dwTime : 0L;
#else
				return "xprintidle".Bash().Al();
#endif
			}
		}

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

				switch (value.ToString().ToLowerInvariant())
				{
					case Keyword_Fast: val = true; break;

					case Keyword_Slow: val = false; break;
				}

				ThreadAccessors.A_TitleMatchModeSpeed = val;
			}
		}

		/// <summary>
		/// The current tray menu object.
		/// </summary>
		public static Menu A_TrayMenu => Script.TheScript.trayMenu;

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
			set => ThreadAccessors.A_WinDelay = value.Al();
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
		/// The most recent reason the script was asked to terminate. This variable is blank unless the script has an OnExit subroutine and that subroutine is currently running or has been called at least once by an exit attempt. See OnExit for details.
		/// </summary>
		internal static object A_ExitReason
		{
			get;
			set;
		} = "";

		/// <summary>
		/// Internal helper to get/set whether the script's icon is frozen due to <see cref="TraySetIcon"/>.
		/// </summary>
		internal static object A_IconFrozen
		{
			get => Script.TheScript.AccessorData.iconFrozen ?? (Script.TheScript.AccessorData.iconFrozen = false).Value;
			set => Script.TheScript.AccessorData.iconFrozen = value.ParseBool();
		}

		/// <summary>
		/// Internal helper to provide the DPI as a percentage.
		/// </summary>
		internal static double A_ScaledScreenDPI => A_ScreenDPI / 96.0;

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
		/// Internal helper to get the executing assembly.
		/// </summary>
		/// <returns>If compiled, the entry assembly, else the executing assembly.</returns>
		internal static Assembly GetAssembly() => CompilerHelper.compiledasm ?? Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();

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

	public static partial class KeysharpEnhancements
	{
		/// <summary>
		/// Whether timers are allowed to operate in the script. Default: true.
		/// </summary>
		public static object A_AllowTimers { get; set; } = true;

		/// <summary>
		/// The command line string used to run the script.
		/// </summary>
		public static string A_CommandLine
		{
			get
			{
				var exe =
#if WINDOWS
					Application.ExecutablePath;
#else
					Environment.ProcessPath ?? string.Empty;
#endif

				if (exe.Contains(' '))
				{
					if (!exe.StartsWith('"'))
						exe = '"' + exe;

					if (!exe.EndsWith('"'))
						exe += '"';
				}

				var args = new List<string>();

				foreach (var arg in Environment.GetCommandLineArgs().Skip(1))
				{
					var quotedArg = arg;

					if (quotedArg.Contains(' '))
					{
						if (!quotedArg.StartsWith('"'))
							quotedArg = '"' + quotedArg;

						if (!quotedArg.EndsWith('"'))
							quotedArg += '"';
					}

					args.Add(quotedArg);
				}

				return args.Count > 0 ? exe + " " + string.Join(' ', args) : exe;
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
		/// Whether the script has exited yet.
		/// </summary>
		public static bool A_HasExited => Script.TheScript.hasExited;

		/// <summary>
		/// The path to Keysharp.Core.Dll
		/// </summary>
		public static string A_KeysharpCorePath => Assembly.GetAssembly(typeof(Accessors)).Location;

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
		public static string A_NowUtcMs => Conversions.ToYYYYMMDDHH24MISSFFF(DateTime.UtcNow);

		/// <summary>
		/// Whether the script is exempt from being able to be suspended.
		/// </summary>
		public static object A_SuspendExempt
		{
			get => Script.TheScript.HotstringManager.hsSuspendExempt;
			set => Script.TheScript.HotstringManager.hsSuspendExempt = ForceBool(value);
		}

		/// <summary>
		/// The total height in pixels of the virtual screen.
		/// </summary>
		public static long A_TotalScreenHeight =>
#if WINDOWS
			SystemInformation.VirtualScreen.Height;
#else
			Forms.Screen.DisplayBounds.Height.Al();
#endif

		/// <summary>
		/// The total width in pixels of the virtual screen.
		/// </summary>
		public static long A_TotalScreenWidth =>
#if WINDOWS
			SystemInformation.VirtualScreen.Width;
#else
			Forms.Screen.DisplayBounds.Width.Al();
#endif

		/// <summary>
		/// The value specified by #UseHook.
		/// </summary>
		public static bool A_UseHook => Script.TheScript.ForceKeybdHook;

		/// <summary>
		/// Whether #WinActivateForce was specified.
		/// </summary>
		public static bool A_WinActivateForce => Script.TheScript.WinActivateForce;

		/// <summary>
		/// The height of the working area of the primary screen.
		/// </summary>
		public static long A_WorkAreaHeight => Forms.Screen.PrimaryScreen.WorkingArea.Height.Al();

		/// <summary>
		/// The width of the working area of the primary screen.
		/// </summary>
		public static long A_WorkAreaWidth => Forms.Screen.PrimaryScreen.WorkingArea.Width.Al();
	}

	/// <summary>
	/// Thread specific variables.
	/// </summary>
	internal static class ThreadAccessors
	{
		/// <summary>
		/// Retrieves the coordinate mode for the specified operation.
		/// </summary>
		/// <param name="mode">The operation to retrieve the coordinate mode for.</param>
		/// <returns>The coordinate mode for the specified operation.</returns>
		/// <exception cref="ValueError">A <see cref="ValueError"/> exception is thrown if an invalid coordinate operation is specified.</exception>
		internal static CoordModeType GetCoordMode(CoordMode mode)
		{
			return mode switch
			{
				CoordMode.Caret => A_CoordModeCaret,
				CoordMode.Menu => A_CoordModeMenu,
				CoordMode.Mouse => A_CoordModeMouse,
				CoordMode.Pixel => A_CoordModePixel,
				CoordMode.Tooltip => A_CoordModeToolTip,
				_ => (CoordModeType)Errors.ValueErrorOccurred($"Invalid coordinate mode type: {mode}", null, CoordModeType.Client)
			};
		}

		/// <summary>
		/// The coordinate mode for positioning the caret.
		/// </summary>
		/// <exception cref="ValueError">A <see cref="ValueError"/> exception is thrown if the value couldn't be converted to a <see cref="CoordModeType"/>.</exception>
		public static CoordModeType A_CoordModeCaret
		{
			get => Script.TheScript.Threads.CurrentThread.configData.coordModeCaret;
			set => Script.TheScript.Threads.CurrentThread.configData.coordModeCaret = value;
		}

		/// <summary>
		/// The coordinate mode for positioning menus.
		/// </summary>
		/// <exception cref="ValueError">A <see cref="ValueError"/> exception is thrown if the value couldn't be converted to a <see cref="CoordModeType"/>.</exception>
		public static CoordModeType A_CoordModeMenu
		{
			get => Script.TheScript.Threads.CurrentThread.configData.coordModeMenu;
			set => Script.TheScript.Threads.CurrentThread.configData.coordModeMenu = value;
		}

		/// <summary>
		/// The coordinate mode for positioning the mouse.
		/// </summary>
		/// <exception cref="ValueError">A <see cref="ValueError"/> exception is thrown if the value couldn't be converted to a <see cref="CoordModeType"/>.</exception>
		public static CoordModeType A_CoordModeMouse
		{
			get => Script.TheScript.Threads.CurrentThread.configData.coordModeMouse;
			set => Script.TheScript.Threads.CurrentThread.configData.coordModeMouse = value;
		}

		/// <summary>
		/// The coordinate mode for positioning pixels.
		/// </summary>
		/// <exception cref="ValueError">A <see cref="ValueError"/> exception is thrown if the value couldn't be converted to a <see cref="CoordModeType"/>.</exception>
		public static CoordModeType A_CoordModePixel
		{
			get => Script.TheScript.Threads.CurrentThread.configData.coordModePixel;
			set => Script.TheScript.Threads.CurrentThread.configData.coordModePixel = value;
		}

		/// <summary>
		/// The coordinate mode for positioning tooltips.
		/// </summary>
		/// <exception cref="ValueError">A <see cref="ValueError"/> exception is thrown if the value couldn't be converted to a <see cref="CoordModeType"/>.</exception>
		public static CoordModeType A_CoordModeToolTip
		{
			get => Script.TheScript.Threads.CurrentThread.configData.coordModeToolTip;
			set => Script.TheScript.Threads.CurrentThread.configData.coordModeToolTip = value;
		}

		/// <summary>
		/// The type of mouse cursor currently being displayed. It will be one of the following words:<br/>
		/// AppStarting, Arrow, Cross, Help, IBeam, Icon, No, Size, SizeAll, SizeNESW, SizeNS, SizeNWSE, SizeWE, UpArrow, Wait, Unknown.<br/>
		/// The acronyms used with the size-type cursors are compass directions, e.g. NESW = NorthEast+SouthWest.<br/>
		/// The hand-shaped cursors (pointing and grabbing) are classified as Unknown.
		/// </summary>
		public static string A_Cursor
		{
			get
			{
#if WINDOWS
				return Cursor.Current is Cursor cur
					? cur.ToString().Trim(BothBrackets).Split(' ', StringSplitOptions.RemoveEmptyEntries)[1].Replace("Cursor", "")
					: "Default---";
#else
				return "Default---";
#endif
			}
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
		/// True if the thread immediately underneath the current thread is paused, otherwise false.
		/// </summary>
		public static bool A_IsPaused
		{
			get => Script.TheScript.Threads.UnderlyingThread?.isPaused ?? false;
			set
			{
				var underlyingThread = Script.TheScript.Threads.UnderlyingThread;
				if (underlyingThread != null) underlyingThread.isPaused = value;
			}
		}

		/// <summary>
		/// Sets the mouse speed that will be used if unspecified in <see cref="Click"/>.
		/// </summary>
		public static long A_DefaultMouseSpeed
		{
			get => Script.TheScript.Threads.CurrentThread.configData.defaultMouseSpeed;
			set => Script.TheScript.Threads.CurrentThread.configData.defaultMouseSpeed = value;
		}

		/// <summary>
		/// The delay for control-modifying functions, in milliseconds. For details, see <see cref="SetControlDelay"/>.
		/// Unused, because no control delays are needed.
		/// </summary>
		internal static long A_ControlDelay
		{
			get => Script.TheScript.Threads.CurrentThread.configData.controlDelay;
			set => Script.TheScript.Threads.CurrentThread.configData.controlDelay = value;
		}

		/// <summary>
		/// Whether invisible text in a window is "seen" for the purpose of finding the window.
		/// </summary>
		internal static bool A_DetectHiddenText
		{
			get => Script.TheScript.Threads.CurrentThread.configData.detectHiddenText;
			set => Script.TheScript.Threads.CurrentThread.configData.detectHiddenText = value;
		}

		/// <summary>
		/// Whether to detect hidden windows.
		/// </summary>
		internal static bool A_DetectHiddenWindows
		{
			get => Script.TheScript.Threads.CurrentThread.configData.detectHiddenWindows;
			set => Script.TheScript.Threads.CurrentThread.configData.detectHiddenWindows = value;
		}

		/// <summary>
		/// Contains event information from various commands.
		/// </summary>
		internal static object A_EventInfo
		{
			get => Script.TheScript.Threads.CurrentThread.eventInfo;
			set => Script.TheScript.Threads.CurrentThread.eventInfo = value;
		}

		/// <summary>
		/// The result from last Windows GetLastError() function call which is stored after certain
		/// function calls such as DllCall and file functions.
		/// </summary>
		internal static int A_LastError
		{
			get => Script.TheScript.Threads.CurrentThread.lastError;
			set => Script.TheScript.Threads.CurrentThread.lastError = value;
		}

		/// <summary>
		/// The default file encoding to use.
		/// </summary>
		internal static object A_FileEncoding
		{
			get
			{
				var fileEncoding = A_FileEncodingRaw;
				var val = fileEncoding.BodyName;

				if (fileEncoding is UnicodeEncoding ue)
				{
					if (ue.GetPreamble().Length == 0)
						val += "-raw";
				}
				else if (fileEncoding is UTF8Encoding u8)
				{
					if (u8.GetPreamble().Length == 0)
						val += "-raw";
				}

				return val;
			}
			set
			{
				Script.TheScript.Threads.CurrentThread.configData.fileEncoding = value is Encoding enc ? enc : Files.GetEncoding(value.ToString());
			}
		}

		/// <summary>
		/// Wrapper to retrieve the file encoding as an <see cref="Encoding"/> object.
		/// </summary>
		internal static Encoding A_FileEncodingRaw => Script.TheScript.Threads.CurrentThread.configData.fileEncoding;

		/// <summary>
		/// The delay in milliseconds between SendEvent keystrokes.
		/// </summary>
		internal static long A_KeyDelay
		{
			get => Script.TheScript.Threads.CurrentThread.configData.keyDelay;
			set => Script.TheScript.Threads.CurrentThread.configData.keyDelay = value;
		}

		/// <summary>
		/// The delay in milliseconds between SendPlay keystrokes.
		/// </summary>
		internal static long A_KeyDelayPlay
		{
			get => Script.TheScript.Threads.CurrentThread.configData.keyDelayPlay;
			set => Script.TheScript.Threads.CurrentThread.configData.keyDelayPlay = value;
		}

		/// <summary>
		/// The delay in milliseconds between SendEvent key down and key release.
		/// </summary>
		internal static long A_KeyDuration
		{
			get => Script.TheScript.Threads.CurrentThread.configData.keyDuration;
			set => Script.TheScript.Threads.CurrentThread.configData.keyDuration = value;
		}

		/// <summary>
		/// The delay in milliseconds between SendPlay key down and key release.
		/// </summary>
		internal static long A_KeyDurationPlay
		{
			get => Script.TheScript.Threads.CurrentThread.configData.keyDurationPlay;
			set => Script.TheScript.Threads.CurrentThread.configData.keyDurationPlay = value;
		}

		/// <summary>
		/// The delay in milliseconds between SendPlay mouse events.
		/// </summary>
		internal static long A_MouseDelay
		{
			get => Script.TheScript.Threads.CurrentThread.configData.mouseDelay;
			set => Script.TheScript.Threads.CurrentThread.configData.mouseDelay = value;
		}

		/// <summary>
		/// The delay in milliseconds between SendEvent mouse events.
		/// </summary>
		internal static long A_MouseDelayPlay
		{
			get => Script.TheScript.Threads.CurrentThread.configData.mouseDelayPlay;
			set => Script.TheScript.Threads.CurrentThread.configData.mouseDelayPlay = value;
		}

		/// <summary>
		/// Thread peek frequency in milliseconds.
		/// Unused because Keysharp is compiled and not interpreted.
		internal static long A_PeekFrequency
		{
			get => Script.TheScript.Threads.CurrentThread.configData.peekFrequency;
			set => Script.TheScript.Threads.CurrentThread.configData.peekFrequency = value;
		}

#if WINDOWS

		/// <summary>
		/// The current registry view, either 32 or 64.
		/// </summary>
		internal static long A_RegView
		{
			get => Script.TheScript.Threads.CurrentThread.configData.regView;
			set => Script.TheScript.Threads.CurrentThread.configData.regView = value;
		}

#endif

		/// <summary>
		/// The send level to use when sending keys.<br/>
		/// The range is 0-100.
		/// </summary>
		internal static long A_SendLevel
		{
			get => Script.TheScript.Threads.CurrentThread.configData.sendLevel;
			set => Script.TheScript.Threads.CurrentThread.configData.sendLevel = value;
		}

		/// <summary>
		/// The send mode to use for sending keys.<br/>
		/// Possible values are Event, Input, Play, and InputThenPlay.
		/// </summary>
		internal static SendModes A_SendMode
		{
			get => Script.TheScript.Threads.CurrentThread.configData.sendMode;
			set => Script.TheScript.Threads.CurrentThread.configData.sendMode = value;
		}

		/// <summary>
		/// Whether to restore the state of CapsLock after a <see cref="Send"/>.
		/// </summary>
		internal static bool A_StoreCapsLockMode
		{
			get => Script.TheScript.Threads.CurrentThread.configData.storeCapsLockMode;
			set => Script.TheScript.Threads.CurrentThread.configData.storeCapsLockMode = value;
		}

		internal static long A_TitleMatchMode
		{
			get => Script.TheScript.Threads.CurrentThread.configData.titleMatchMode;
			set => Script.TheScript.Threads.CurrentThread.configData.titleMatchMode = value;
		}

		internal static bool A_TitleMatchModeSpeed
		{
			get => Script.TheScript.Threads.CurrentThread.configData.titleMatchModeSpeed;
			set => Script.TheScript.Threads.CurrentThread.configData.titleMatchModeSpeed = value;
		}

		/// <summary>
		/// The current delay set by <see cref="SetWinDelay"/>.
		/// </summary>
		internal static long A_WinDelay
		{
			get => Script.TheScript.Threads.CurrentThread.configData.winDelay;
			set => Script.TheScript.Threads.CurrentThread.configData.winDelay = value;
		}
	}
}