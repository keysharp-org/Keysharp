//#define SEPARATE_KB_THREAD

[assembly: InternalsVisibleTo("Keysharp.Tests")]
[assembly: InternalsVisibleTo("Keysharp.Benchmark")]
[assembly: InternalsVisibleTo("Keyview")]

namespace Keysharp.Scripting
{
	/// <summary>
	/// This is the main script object which contains all instance data needed for a script to run.
	/// A Script object is created twice: once for parsing, and another for running.
	/// The design is unusual because all instance data is contained here, then the object itself
	/// is assigned to a global static member of itself, script.
	/// The reason for this is that most of the user facing functions in Keysharp are static.
	/// However, just having them access static data presents a major problem:
	///     Static data is left around after multiple instances are created during parsing, running
	///     and between unit tests. As long as they all exist in the same process, each instance does
	///     not start clean and instead starts with unpredictable remnants of the previous instance.
	/// To remedy this problem, all data is instance data, and there is only one static member that all
	/// instance data is accessed through. This ensures a clean start every time we create a Script object.
	/// </summary>
	public partial class Script
	{
		internal static bool dpimodeset;//This should be done once per process, so it can be static.
#if LINUX
		private static Encoding enc1252 = Encoding.Default;
#endif
		public const char dotNetMajorVersion = '9';
		internal System.Timers.Timer tickTimer = new System.Timers.Timer(SLEEP_INTERVAL * 4);
		internal MessageFilter msgFilter;
		internal volatile bool loopShouldDoEvents = false;
		internal volatile bool hasExited = false;
		public bool ForceKeybdHook;
		public string[] ScriptArgs = [];
		public string[] KeysharpArgs = [];
		public uint MaxThreadsTotal = 12u;
		public bool NoTrayIcon = false;
		public bool ValidateThenExit;
		public bool WinActivateForce = false;
		//Some unit tests use try..catch in non-script code, which causes ErrorOccurred to display the error dialog.
		//This allows to suppress it, but only inside ErrorOccurred (not in TryCatch etc).
		public bool SuppressErrorOccurredDialog = AppDomain.CurrentDomain.FriendlyName == "testhost";
		//This allows to suppress all error processing
		public uint SuppressErrorOccurred = 0;
		internal const double DefaultErrorDouble = double.NaN;
		internal const int DefaultErrorInt = int.MinValue;
		internal const long DefaultErrorLong = long.MinValue;
		internal const string DefaultNewLine = "\n";
		internal const string DefaultObject = "";
		internal const string DefaultErrorObject = "";
		internal const string DefaultErrorString = "";
		internal const int INTERVAL_UNSPECIFIED = int.MinValue + 303;
		internal const int maxThreadsLimit = 0xFF;
		internal const int SLEEP_INTERVAL = 10;
		internal const int SLEEP_INTERVAL_HALF = SLEEP_INTERVAL / 2;
		internal List<IFuncObj> ClipFunctions = [];
		internal List<IFuncObj> hotCriterions = [];
		internal nint hotExprLFW = 0;
		internal List<IFuncObj> hotExprs = [];
		internal InputType input;
		internal int inputBeforeHotkeysCount;
		internal DateTime inputTimeoutAt = DateTime.UtcNow;
		internal bool inputTimerExists;
		internal DateTime lastPeekTime;
		internal MainWindow mainWindow;
		internal Gui mainWindowGui;
		internal MenuType menuIsVisible = MenuType.None;
		internal PlatformManagerBase mgr;
		internal int nMessageBoxes;
		internal List<IFuncObj> onErrorHandlers;
		internal List<IFuncObj> onExitHandlers = [];
		private Icon _normalIcon = null;
		public Icon normalIcon
		{
			get
			{
#if WINDOWS
				if (_normalIcon == null)
					_normalIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
#endif
				return _normalIcon ??= ImageHelper.IconFromByteArray(Core.Properties.Resources.Keysharp_ico);
			}
		}
		private Icon _pausedIcon;
		internal Icon pausedIcon => _pausedIcon ??= ImageHelper.IconFromByteArray(Core.Properties.Resources.Keysharp_p_ico);
		private Icon _suspendedIcon;
		internal Icon suspendedIcon => _suspendedIcon ??= ImageHelper.IconFromByteArray(Core.Properties.Resources.Keysharp_s_ico);
		internal bool persistent;
		internal nint playbackHook = 0;
		internal DateTime priorHotkeyStartTime = DateTime.UtcNow;
		public string scriptName = "";
		internal string thisHotkeyName, priorHotkeyName;
		internal DateTime thisHotkeyStartTime;
		internal ThreadLocal<Threads> threads;
		internal DateTime timeLastInputKeyboard;
		internal DateTime timeLastInputMouse;
		internal DateTime timeLastInputPhysical = DateTime.UtcNow;
		internal int totalExistingThreads;//Even though the thread stacks are on a per-real-thread basis, we keep a global count of threads. This may need to change in the future.
		internal int uninterruptibleTime = 17;
		private static int instanceCount;
		private AccessorData accessorData;
		private ArrayIndexValueIteratorData arrayIndexValueIteratorData;
		private ClrIteratorData managedDotNetIteratorData;
#if WINDOWS
		private ComArrayIndexValueEnumeratorData comArrayIndexValueEnumeratorData;
		private ComEnumeratorData comEnumeratorData;
		private ComMethodData comMethodData;
#endif
		private ControlProvider controlProvider;
		private DllData dllData;
		private DriveTypeMapper driveTypeMapper;
		private ExecutableMemoryPoolManager exeMemoryPoolManager;
		private FlowData flowData;
		private FunctionData functionData;
		private GuiData guiData;
		private HotkeyData hotkeyData;
		private HotstringManager hotstringManager;
		private ImageListData imageListData;
		private InputData inputData;
		private bool isReadyToExecute;
		private JoystickData joystickData;
		private KeyboardData keyboardData;
		private KeyboardUtilsData keyboardUtilsData;
		private LoopData loopData;
		private nint mainWindowHandle;
		private MapKeyValueIteratorData mapKeyValueIteratorData;
		private OwnPropsIteratorData ownPropsIteratorData;
		private PlatformProvider platformProvider;
		private ProcessesData processesData;
		private RegExData regExData;
		private RegExIteratorData regExIteratorData;
		private RegExIteratorDataCs regExIteratorDataCs;
		private StringsData stringsData;
		private ToolTipData toolTipData;
		private WindowProvider windowProvider;

		public static Keysharp.Scripting.Script TheScript { get; private set; }
		public Type ProgramType;
		public string ProgramNamespace = Keywords.MainNamespaceName;
		public HotstringManager HotstringManager => hotstringManager ?? (hotstringManager = new ());
		public Threads Threads => threads.Value;
		public Variables Vars { get; private set; }
		internal AccessorData AccessorData => accessorData ?? (accessorData = new ());
		internal ArrayIndexValueIteratorData ArrayIndexValueIteratorData => arrayIndexValueIteratorData ?? (arrayIndexValueIteratorData = new ());
		internal ClrIteratorData ManagedDotNetIteratorData => managedDotNetIteratorData ?? (managedDotNetIteratorData = new());
#if WINDOWS
		internal ComArrayIndexValueEnumeratorData ComArrayIndexValueEnumeratorData => comArrayIndexValueEnumeratorData ?? (comArrayIndexValueEnumeratorData = new ());
		internal ComEnumeratorData ComEnumeratorData => comEnumeratorData ?? (comEnumeratorData = new ());
		internal ComMethodData ComMethodData => comMethodData ?? (comMethodData = new ());
#endif
		internal ControlProvider ControlProvider => controlProvider ?? (controlProvider = new ());
		internal DllData DllData => dllData ?? (dllData = new ());
		internal DriveTypeMapper DriveTypeMapper => driveTypeMapper ?? (driveTypeMapper = new ());
		internal ExecutableMemoryPoolManager ExecutableMemoryPoolManager => exeMemoryPoolManager ?? (exeMemoryPoolManager = new ());
		internal FlowData FlowData => flowData ?? (flowData = new ());
		internal FunctionData FunctionData => functionData ?? (functionData = new ());
		internal GuiData GuiData => guiData ?? (guiData = new ());
		internal HookThread HookThread { get; private set; }
		internal HotkeyData HotkeyData => hotkeyData ?? (hotkeyData = new ());

		internal long HwndLastUsed
		{
			get => Threads.CurrentThread.hwndLastUsed;
			set => Threads.CurrentThread.hwndLastUsed = value;
		}

		internal ImageListData ImageListData => imageListData ?? (imageListData = new ());
		internal InputData InputData => inputData ?? (inputData = new ());
		internal bool IsMainWindowClosing => mainWindow == null || mainWindow.IsClosing;
		internal bool IsReadyToExecute => isReadyToExecute;
		internal JoystickData JoystickData => joystickData ?? (joystickData = new ());
		internal KeyboardData KeyboardData => keyboardData ?? (keyboardData = new ());
		internal KeyboardUtilsData KeyboardUtilsData => keyboardUtilsData ?? (keyboardUtilsData = new ());
		internal LoopData LoopData => loopData ?? (loopData = new ());

		internal nint MainWindowHandle
		{
			get
			{
				if (mainWindow == null)
					return 0;

				if (mainWindowHandle == 0)
					_ = mainWindow.Invoke(() => mainWindowHandle = mainWindow.Handle);

				return mainWindowHandle;
			}
		}

		internal MapKeyValueIteratorData MapKeyValueIteratorData => mapKeyValueIteratorData ?? (mapKeyValueIteratorData = new ());
		internal OwnPropsIteratorData OwnPropsIteratorData => ownPropsIteratorData ?? (ownPropsIteratorData = new ());
		internal PlatformProvider PlatformProvider => platformProvider ?? (platformProvider = new ());
		internal ProcessesData ProcessesData => processesData ?? (processesData = new ());
		internal Reflections Reflections { get; private set; }
		internal ReflectionsData ReflectionsData { get; } = new ();//Don't lazy initialize, it's always needed in every Script.TheScript.
		internal RegExData RegExData => regExData ?? (regExData = new ());
		internal RegExIteratorData RegExIteratorData => regExIteratorData ?? (regExIteratorData = new ());
		internal RegExIteratorDataCs RegExIteratorDataCs => regExIteratorDataCs ?? (regExIteratorDataCs = new ());
		internal StringsData StringsData => stringsData ?? (stringsData = new ());
		internal ToolTipData ToolTipData => toolTipData ?? (toolTipData = new ());
		internal WindowProvider WindowProvider => windowProvider ?? (windowProvider = new ());

#if LINUX
		internal string ldLibraryPath = Environment.GetEnvironmentVariable("LD_LIBRARY_PATH") ?? "";
#endif

		static Script()
		{
			// Needed for string and file encodings such as Windows-1252
			Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
#if WINDOWS
			Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

			Application.ThreadException += (s, e) =>
			{
				if (e.Exception is Flow.UserRequestedExitException) return; // silence during shutdown
				System.Diagnostics.Debug.Write("ThreadException caught: " + e.Exception);
			};
#endif

			AppDomain.CurrentDomain.UnhandledException += (s, e) =>
			{
				var ex = e.ExceptionObject as Exception;
				if (Script.TheScript?.hasExited == true || ex is Flow.UserRequestedExitException) return; // silence during shutdown
				System.Diagnostics.Debug.WriteLine("Exception caught in current domain: " + ex);
			};

			WindowX.SetProcessDPIAware();
			CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
			CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
#if LINUX
			Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);//For some reason, linux needs this for rich text to work.
			enc1252 = Encoding.GetEncoding(1252);
			if (Application.Instance == null)
			{
				try
				{
					_ = new Application();
					while (Application.Instance == null)
						System.Threading.Thread.Sleep(1);
				}
				catch (Exception ex)
				{
					System.Diagnostics.Debug.WriteLine("Failed to initialize Eto Application: " + ex);
				}
			}

			Application.Instance.UnhandledException += (s, e) =>
			{
				if (e.ExceptionObject is Flow.UserRequestedExitException) return; // silence during shutdown
				System.Diagnostics.Debug.Write("ThreadException caught: " + e.ExceptionObject);
			};

			try
			{
				var settings = Gtk.Settings.Default;
				if (settings != null)
				{
					settings.SetProperty("gtk-menu-images", new GLib.Value(true));
					settings.SetProperty("gtk-button-images", new GLib.Value(true));
				}
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine("Failed to enable GTK menu images: " + ex);
			}
#endif
			SetInitialFloatFormat();//This must be done intially and not just when A_FormatFloat is referenced for the first time.

#if WINDOWS
			// Temporary patch to override the GdiPlus initialization settings used by WinForms
			// (or more specifically System.Drawing). If StartupParameters is not 4 then external
			// codecs such as webp cannot be used.
			EnsureGdiPlus();
#endif
		}

#if WINDOWS
		static nint _gdiToken;
		public static void EnsureGdiPlus()
		{
			if (_gdiToken != 0) return;
			var si = new WindowsAPI.GdiplusStartupInputEx
			{
				GdiplusVersion = 2,
				SuppressBackgroundThread = false,
				SuppressExternalCodecs = false,
				StartupParameters = 4
			};
			int s = WindowsAPI.GdiplusStartup(out _gdiToken, ref si, IntPtr.Zero);
			if (s != 0) throw new ExternalException($"GdiplusStartup failed: {s}");
		}
#endif

		public Script(Type program = null, string hookMutexName = null)
		{
			ProgramType = program ?? GetCallingType();
			ProgramNamespace = ProgramType.Namespace;
			Script.TheScript = this;//Everywhere in the script will reference this.
			timeLastInputPhysical = DateTime.UtcNow;
			timeLastInputKeyboard = timeLastInputPhysical;
			timeLastInputMouse = timeLastInputPhysical;
			
			//Init the API classes, passing in this which will be used to access their respective data objects.
			Reflections = new Reflections();
			Vars = new Variables();
			Vars.InitClasses();

			threads = new(() => new());

			_ = Script.TheScript.Threads.PushThreadVariables(0, true, false, true);//Ensure there is always one thread in existence for reference purposes, but do not increment the actual thread counter.
			var pd = this.ProcessesData;
			pd.MainThreadID = CurrentThreadId();
			pd.ManagedMainThreadID = Thread.CurrentThread.ManagedThreadId;//Figure out how to do this on linux.//TODO

#if WINDOWS
			msgFilter = new MessageFilter(this);
			Application.AddMessageFilter(msgFilter);
#else
			msgFilter = new MessageFilter(this);
			msgFilter.Attach();
#endif
			if (hookMutexName != null && hookMutexName != "") HookThread.MutexName = hookMutexName;
			_ = InitHook();//Why is this always being initialized even when there are no hooks? This is very inefficient.//TODO
			//Init the data objects that the API classes will use.
			SetInitialFloatFormat();//This must be done intially and not just when A_FormatFloat is referenced for the first time.
			tickTimer.Elapsed += TickTimerCallback;
			tickTimer.AutoReset = false;
			tickTimer.Start();
		}
		
		~Script()
		{
			tickTimer?.Dispose();
#if WINDOWS
			Application.RemoveMessageFilter(msgFilter);
#elif LINUX
			msgFilter?.Detach();
#endif
		}

		[MethodImpl(MethodImplOptions.NoInlining)]  // prevent inlining from collapsing frames
		public static Type GetCallingType(int skipFrames = 2)
		{
			var st = new StackTrace();
			// skip the requested frames (defaults: 0=this, 1=GetCallingType, 2=your caller)
			var frame = st.GetFrame(skipFrames);
			if (frame == null) return null;

			var method = frame.GetMethod();
			return method.DeclaringType;
		}

		[DebuggerStepThrough]
		void TickTimerCallback(object sender, EventArgs e)
		{
			loopShouldDoEvents = true;
		}

		/// <summary>
		/// Will be a generated call within Main which calls into this class to add DLLs.
		/// </summary>
		/// <param name="p"></param>
		/// <param name="s"></param>
		public void LoadDll(string library, bool throwOnFailure = true)
		{
			if (library.Length == 0)
			{
				if (!SetDllDirectory(null))//An empty #DllLoad restores the default search order.
					if (throwOnFailure)
					{
						_ = Errors.ErrorOccurred("PlatformManager.SetDllDirectory(null) failed.", null, Keyword_ExitApp);
						return;
					}
			}
			else if (Directory.Exists(library))
			{
				if (!SetDllDirectory(library))
					if (throwOnFailure)
					{
						_ = Errors.ErrorOccurred($"PlatformManager.SetDllDirectory({library}) failed.", null, Keyword_ExitApp);
						return;
					}
			}
			else
			{
				var libraryName = library;
				if (libraryName.Length != 0 && !Path.HasExtension(libraryName)
#if !WINDOWS
					&& !File.Exists(libraryName)
#endif
				)
					libraryName += Keywords.LibraryExtension;

			
				var hmodule = LoadLibrary(libraryName);

				if (hmodule != 0)
				{
#if WINDOWS
					// "Pin" the dll so that the script cannot unload it with FreeLibrary.
					// This is done to avoid undefined behavior when DllCall optimizations
					// resolves a proc address in a dll loaded by this directive.
					_ = WindowsAPI.GetModuleHandleEx(WindowsAPI.GET_MODULE_HANDLE_EX_FLAG_PIN, libraryName, out hmodule);  // MSDN regarding hmodule: "If the function fails, this parameter is NULL."
#endif
				}
				else if (throwOnFailure)
				{
					_ = Errors.ErrorOccurred($"Failed to load DLL {libraryName}.", null, Keyword_ExitApp);
					return;
				}
			}
		}

		public static bool HandleSingleInstance(string title, eScriptInstance inst)
		{
			if (title.Length == 0 || title == "*")//Happens when running in Keyview.
				return false;

			if (Env.FindCommandLineArg("force") != null || Env.FindCommandLineArg("f") != null)
				inst = eScriptInstance.Off;

			if (Env.FindCommandLineArg("restart") != null || Env.FindCommandLineArg("r") != null)
				inst = eScriptInstance.Force;

			var exit = false;
			var oldDetect = WindowX.DetectHiddenWindows(true);
			var oldMatchMode = WindowX.SetTitleMatchMode(3);//Require exact match.

			switch (inst)
			{
				case eScriptInstance.Force:
				{
					_ = WindowX.WinClose(title, "", 2);
				}
				break;

				case eScriptInstance.Ignore:
					if (WindowX.WinExist(title) != 0)
						exit = true;

					break;

				case eScriptInstance.Off:
					break;

				case eScriptInstance.Prompt:
				default:
					var hwnd = WindowX.WinExist(title);

					if (hwnd != 0)
					{
						if (Dialogs.MsgBox("Do you want to close the existing instance before running this one?\nYes to exit that instance, No to exit this instance.", "", "YesNo") == "Yes")
							_ = WindowX.WinClose(hwnd, "", 2);
						else
							exit = true;
					}

					break;
			}

			_ = WindowX.SetTitleMatchMode(oldMatchMode);
			_ = WindowX.DetectHiddenWindows(oldDetect);
			return exit;
		}

		public void ExitIfNotPersistent(Flow.ExitReasons exitReason = Flow.ExitReasons.Exit)
		{
			//Must use BeginInvoke() because this might be called from _ks_UserMainCode(),
			//so it needs to run after that thread has exited.
			if (!IsMainWindowClosing && totalExistingThreads == 0)
			{
				mainWindow?.CheckedBeginInvoke(() =>
				{
					if (!IsMainWindowClosing && !AnyPersistent())
						_ = Flow.ExitAppInternal(exitReason, Environment.ExitCode, false);
				}, true, false);
			}
		}

		public string GetPublicStaticPropertyNames()
		{
			var l1 = ReflectionsData.flatPublicStaticMethods.Keys.ToList();
			l1.AddRange(ReflectionsData.flatPublicStaticProperties.Keys);
			var hs = new HashSet<string>(l1);
			return string.Join(' ', hs);
		}

		public void RunMainWindow(string title, Func<object> userInit, bool _persistent)
		{
			mainWindow = new MainWindow();

			if (!string.IsNullOrEmpty(title))
				mainWindow.Text = title;

			mainWindow.ClipboardUpdate += PrivateClipboardUpdate;
			if (normalIcon != null)
				mainWindow.Icon = normalIcon;
			persistent = _persistent;
			mainWindowGui = new Gui(null, null, null, mainWindow);
			//Only do these on Windows, because it seems to have the opposite effect on linux:
			//The main window is always shown on startup, but in a broken non-drawn state.
			mainWindow.AllowShowDisplay = false; // Prevent show on script startup
			mainWindow.ShowInTaskbar = true; // Without this the main window won't have a taskbar icon
			
			_ = mainWindow.BeginInvoke(() =>
			{
				var ret = Threads.BeginThread();
				// Replace configData with the prototype, which will later be used to initialize all
				// subsequent pseudo-threads. After the auto-execute thread has finished, restore the
				// original configData object because the thread variables object may be reused later.
				var prevConfigData = ret.Item2.configData;
				ret.Item2.configData = AccessorData.threadConfigDataPrototype;
				bool autoExecResult = Flow.TryCatch(() =>
				{
					_ = userInit();
					//HotkeyDefinition.ManifestAllHotkeysHotstringsHooks() will be called inside of userInit() because it
					//must be done:
					//  After the window handle is created and the handle isn't valid until mainWindow.Load() is called.
					//  Also right after all hotkeys and hotstrings are created.
					isReadyToExecute = true;
				}, false, ret);
				ret.Item2.configData = prevConfigData;
				_ = Threads.EndThread(ret);

				if (!autoExecResult)
				{
					if (!persistent)//An exception was thrown so the generated ExitApp() call in _ks_UserMainCode() will not have been called, so call it here.
					{
						_ = Flow.ExitApp(1);
					}
				}

				ExitIfNotPersistent();
			});
#if WINDOWS
			Application.Run(mainWindow);
#else
			Application.Instance.Run(mainWindow);
#endif
		}

		public void SetName(string s)
        {
			scriptName = s;

			//If we're running via passing in a script and are not in a unit test, then set the working directory to that of the script file.
			var path = Path.GetFileName(
#if WINDOWS
				Application.ExecutablePath
#else
				Environment.ProcessPath ?? string.Empty
#endif
				).ToLowerInvariant();

			if (path != "testhost.exe" && path != "testhost.dll" && !A_IsCompiled)
				_ = Dir.SetWorkingDir(A_ScriptDir);
        } 

		public void SetReady() => isReadyToExecute = true;

		//public static void TestSomething()
		//{
		//  char[] SpaceTab = " \t".ToCharArray();
		//  SearchValues<char> SpaceTabSv = SearchValues.Create(SpaceTab);
		//  //var splits = val.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		//  var width = int.MinValue;
		//  var height = int.MinValue;
		//  var icon = "";
		//  object iconnumber = 0;
		//  var filename = "";// splits.Last();
		//  var val = "*w100 *h200 *icon2 C:\a filename.jpg";

		//  //for (var i = 0; i < splits.Length - 1; i++)
		//  foreach (Range r in val.AsSpan().SplitAny(SpaceTabSv))
		//  {
		//      var opt = val.AsSpan(r).Trim();

		//      if (Options.TryParse(opt, "*w", ref width)) { }
		//      else if (Options.TryParse(opt, "*h", ref height)) { }
		//      else if (Options.TryParseString(opt, "*icon", ref icon)) { iconnumber = ImageHelper.PrepareIconNumber(icon); }
		//      else
		//      {
		//          filename = val.Substring(r.Start.Value);
		//          break;
		//      }
		//  }
		//}

		/*
		    internal static string GetVariableInfo()
		    {
		    var sb = new StringBuilder(2048);
		    var stack = new StackTrace(false).GetFrames();

		    for (var i = stack.Length - 1; i >= 0; i--)
		    {
		        if (stack[i] != null &&
		                stack[i].GetMethod() != null &&
		                stack[i].GetMethod().DeclaringType.Attributes.HasFlag(TypeAttributes.Public))//Public is the script, everything else should be hidden.
		        {
		            if (stack[i].GetMethod().DeclaringType.Namespace != null &&
		                    stack[i].GetMethod().DeclaringType.Namespace.StartsWith("Keysharp", StringComparison.OrdinalIgnoreCase))
		            {
		                var meth = stack[i].GetMethod();
		                _ = sb.AppendLine($"Class: {meth.ReflectedType.Name}");
		                _ = sb.AppendLine();

		                foreach (var v in meth.ReflectedType.GetProperties(BindingFlags.Public | BindingFlags.Static))
		                {
		                    var val = v.GetValue(null);
		                    var varstr = $"\t{val?.GetType()} {v.Name}: ";

		                    if (val is string s)
		                        varstr += $"[{s.Length}] {s.Substring(0, Math.Min(s.Length, 60))}";
		                    else if (val is Keysharp.Core.Array arr)
		                    {
		                        var ct = Math.Min(100, arr.Count);
		                        var tempsb = new StringBuilder(ct * 100);

		                        for (var a = 1; a <= ct; a++)
		                        {
		                            var tempstr = arr[a].ToString();
		                            _ = tempsb.Append(tempstr.Substring(0, Math.Min(tempstr.Length, 60)));

		                            if (a < ct)
		                                _ = tempsb.Append(", ");
		                        }

		                        varstr += tempsb.ToString();
		                    }
		                    else if (val is Keysharp.Core.Map map)
		                    {
		                        var ct = Math.Min(100, map.Count);
		                        var a = 0;
		                        var tempsb = new StringBuilder(ct * 100);
		                        _ = tempsb.Append('{');

		                        foreach (var kv in map.map)
		                        {
		                            var tempstr = kv.Value.ToString();
		                            _ = tempsb.Append($"{kv.Key} : {tempstr.Substring(0, Math.Min(tempstr.Length, 60))}");

		                            if (++a < ct)
		                                _ = tempsb.Append(", ");
		                        }

		                        _ = tempsb.Append('}');
		                        varstr += tempsb.ToString();
		                    }
		                    else if (val == null)
		                        varstr += "null";
		                    else
		                        varstr += val.ToString();

		                    _ = sb.AppendLine(varstr);
		                }

		                _ = sb.AppendLine("");
		                _ = sb.AppendLine($"Method: {meth.Name}");
		                var mb = stack[i].GetMethod().GetMethodBody();

		                foreach (var lvi in mb.LocalVariables)
		                    _ = sb.AppendLine($"\t{lvi}");

		                _ = sb.AppendLine("--------------------------------------------------");
		                _ = sb.AppendLine();
		            }
		        }
		    }

		    return sb.ToString();
		    }
		*/

		public void SimulateKeyPress(uint key) => HookThread.SimulateKeyPress(key);

		public void Stop()
		{
			HookThread?.Stop();
			stringsData?.Free();

			if (!IsMainWindowClosing)
			{
				mainWindow.CheckedInvoke(() =>
				{
					mainWindow.Close();
					mainWindow = null;
				}, false);
			}

#if WINDOWS
			if (Tray != null && Tray.ContextMenuStrip != null)
			{
				Tray.ContextMenuStrip.CheckedInvoke(() =>
				{
					Tray.Visible = false;
					Tray.Dispose();
					Tray = null;
				}, true);
			}
#endif
		}

		public override string ToString()
		{
			return $"Script {scriptName} {instanceCount++}";
		}

		public void VerifyVersion(string ver, bool plus, int line, string code)
		{
			var ahkver = A_AhkVersion;
			var reqvers = ParseVersionToInts(ver);
			var thisvers = ParseVersionToInts(ahkver);

			for (var i = 0; i < 4; i++)
			{
				if (plus)
				{
					if (reqvers[i] > thisvers[i])
						throw new ParseException($"This script requires Keysharp >= v{ver}, but you have v{ahkver}", line, code);
				}
				else if (reqvers[i] != thisvers[i])
					throw new ParseException($"This script requires Keysharp == v{ver}, but you have v{ahkver}", line, code);

				if (thisvers[i] > reqvers[i])
					break;
			}
		}

		public void WaitThreads()
		{
			//Check against 1 instead of 0, because this may be launched in a thread as a result of a hotkey.
			//If this gets stuck in a loop it means we have a thread imbalance/mismatch somewhere.
			//We added them, but never removed. While seemingly dangerous to have, it's a handy
			//way to know we've found a bug.
			while (totalExistingThreads > 1)
				_ = Flow.Sleep(200);
		}

		internal static string MakeTitleWithVersion(string title) => title + " - Keysharp " + A_AhkVersion;

		internal static int[] ParseVersionToInts(string ver)
		{
			var i = 0;
			var vers = new int[] { 0, 0, 0, 0 };

			foreach (Range r in ver.AsSpan().Split('.'))
			{
				var split = ver.AsSpan(r).Trim();

				if (split.Length > 0)
				{
					if (int.TryParse(split, out var v))
						vers[i] = v;

					i++;
				}
			}

			return vers;
		}

		internal static void SetInitialFloatFormat()
		{
			var t = Thread.CurrentThread;
			var ci = new CultureInfo(t.CurrentCulture.Name);
			ci.NumberFormat.NumberDecimalDigits = 6;
			t.CurrentCulture = ci;
		}

		internal bool AnyPersistent()
		{
			if (totalExistingThreads > 0)
				return true;

			if (Gui.AnyExistingVisibleWindows())
				return true;

			if (HotkeyData.shk.Count > 0)
				return true;

			if (HotstringManager.shs.Count > 0)
				return true;

			if (!FlowData.timers.IsEmpty)
				return true;

			if (ClipFunctions.Count > 0)
				return true;

			if (FlowData.persistentValueSetByUser)
				return true;

			if (input != null)
			{
				for (var i = input; ; i = i.prev)
				{
					if (i != null)
						return true;
				}
			}

			return false;
		}

		internal ResultType IsCycleComplete(int aSleepDuration, DateTime aStartTime, bool aAllowEarlyReturn)
		// This function is used just to make MsgSleep() more readable/understandable.
		{
			var kbdMouseSender = HookThread.kbdMsSender;//This should always be non-null if any hotkeys/strings are present.
			// Note: Even if TickCount has wrapped due to system being up more than about 49 days,
			// DWORD subtraction still gives the right answer as long as aStartTime itself isn't more
			// than about 49 days ago. Note: must cast to int or any negative result will be lost
			// due to DWORD type:
			var tick_now = DateTime.UtcNow;

			if (!aAllowEarlyReturn && (int)(aSleepDuration - (tick_now - aStartTime).TotalMilliseconds) > SLEEP_INTERVAL_HALF)
				// Early return isn't allowed and the time remaining is large enough that we need to
				// wait some more (small amounts of remaining time can't be effectively waited for
				// due to the 10ms granularity limit of SetTimer):
				return ResultType.Fail; // Tell the caller to wait some more.

			// v1.0.38.04: Reset mLastPeekTime because caller has just done a GetMessage() or PeekMessage(),
			// both of which should have routed events to the keyboard/mouse hooks like LONG_OPERATION_UPDATE's
			// PeekMessage() and thus satisfied the reason that mLastPeekTime is tracked in the first place.
			// UPDATE: Although the hooks now have a dedicated thread, there's a good chance mLastPeekTime is
			// beneficial in terms of increasing GUI & script responsiveness, so it is kept.
			// The following might also improve performance slightly by avoiding extra Peek() calls, while also
			// reducing premature thread interruptions.
			lastPeekTime = tick_now;
			return ResultType.Ok;
		}

		internal void SetHotNamesAndTimes(string name)
		{
			// Just prior to launching the hotkey, update these values to support built-in
			// variables such as A_TimeSincePriorHotkey:
			priorHotkeyName = thisHotkeyName;//None of this will work until we come up with a way to manage thread order.//TODO
			priorHotkeyStartTime = thisHotkeyStartTime;
			// Unlike hotkeys -- which can have a name independent of their label by being created or updated
			// with the HOTKEY command -- a hot string's unique name is always its label since that includes
			// the options that distinguish between (for example) :c:ahk:: and ::ahk::
			thisHotkeyName = name;
			thisHotkeyStartTime = DateTime.UtcNow; // Fixed for v1.0.35.10 to not happen for GUI
		}

		private bool InitHook()
		{
			if (HookThread != null && HookThread.IsHookThreadRunning() && HookThread.IsReadThreadRunning())
				return false;

#if WINDOWS
			HookThread = new WindowsHookThread();
#elif LINUX
			HookThread = new LinuxHookThread();
#else
			return false;
#endif
			return true;
		}

		private void PrivateClipboardUpdate(params object[] o)
		{
			var i = 0;
			var b = false;//False means keep going, true means stop.
#if WINDOWS
			if (Clipboard.ContainsText() || Clipboard.ContainsFileDropList())
#else
			if (Clipboard.Instance.ContainsText)
#endif
				while (!b && i < ClipFunctions.Count) b = IfTest(ClipFunctions[i++].Call(1));//Can't use foreach because the collection could be modified by the event.
			else if (!KeysharpEnhancements.IsClipboardEmpty())
				while (!b && i < ClipFunctions.Count) b = IfTest(ClipFunctions[i++].Call(2));
			else
				while (!b && i < ClipFunctions.Count) b = IfTest(ClipFunctions[i++].Call(0));
		}

		internal Type GetNativeType(Any obj)
		{
			while (obj != null)
			{
				var t = obj.GetType();
				if (!string.Equals(t.Namespace, ProgramNamespace, StringComparison.OrdinalIgnoreCase))
				{
					// we found a built‑in prototype object
					if (t == typeof(Class)) return typeof(KeysharpObject);
					return t;
				}

				// follow the “base” link:
				obj = obj.Base;
			}
			// fallback?
			return typeof(Any);
		}
	}
}
