//#define SEPARATE_KB_THREAD

namespace Keysharp.Scripting;

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
	public const string dotNetMajorVersion = "10";
	internal System.Timers.Timer tickTimer = new(SLEEP_INTERVAL * 4);
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
	internal Icon pausedIcon;
	internal bool persistent;
	internal nint playbackHook = 0;
	internal DateTime priorHotkeyStartTime = DateTime.UtcNow;
	internal string scriptName = "";
	internal Icon suspendedIcon;
	internal string thisHotkeyName, priorHotkeyName;
	internal DateTime thisHotkeyStartTime;
	internal ThreadLocal<Threads> threads;
	internal DateTime timeLastInputKeyboard;
	internal DateTime timeLastInputMouse;
	internal DateTime timeLastInputPhysical = DateTime.UtcNow;
	internal int totalExistingThreads;//Even though the thread stacks are on a per-real-thread basis, we keep a global count of threads. This may need to change in the future.
	internal int uninterruptibleTime = 17;
	private static int instanceCount;
#if WINDOWS
#endif
#if WINDOWS
#endif
	private FlowData flowData;
	private StringsData stringsData;

	[PublicForTestOnly]
	public static Keysharp.Scripting.Script TheScript { get; private set; }
	public HotstringManager HotstringManager => field ??= new();
	public Threads Threads => threads.Value;
	public Variables Vars { get; private set; }
	internal AccessorData AccessorData => field ??= new();
	internal ArrayIndexValueIteratorData ArrayIndexValueIteratorData => field ??= new();
#if WINDOWS
	internal ComArrayIndexValueEnumeratorData ComArrayIndexValueEnumeratorData => field ??= new();
	internal ComEnumeratorData ComEnumeratorData => field ??= new();
	internal ComMethodData ComMethodData => field ??= new();
#endif
	internal ControlProvider ControlProvider => field ??= new();
#if WINDOWS
	internal DllData DllData => field ??= new();
#endif
	internal DriveTypeMapper DriveTypeMapper => field ??= new();
	internal ExecutableMemoryPoolManager ExecutableMemoryPoolManager => field ??= new();
	internal FlowData FlowData => flowData ??= new();
	internal FunctionData FunctionData => field ??= new();
	internal GuiData GuiData => field ??= new();
	internal HookThread HookThread { get; private set; }
	internal HotkeyData HotkeyData => field ??= new();

	internal long HwndLastUsed
	{
		get => Threads.CurrentThread.hwndLastUsed;
		set => Threads.CurrentThread.hwndLastUsed = value;
	}

	internal ImageListData ImageListData => field ??= new();
	internal InputData InputData => field ??= new();
	internal bool IsMainWindowClosing => mainWindow == null || mainWindow.IsClosing;
	internal bool IsReadyToExecute { get; private set; }
	internal JoystickData JoystickData => field ??= new();
	internal KeyboardData KeyboardData => field ??= new();
	internal KeyboardUtilsData KeyboardUtilsData => field ??= new();
	internal LoopData LoopData => field ??= new();

	internal nint MainWindowHandle
	{
		get
		{
			if (mainWindow == null)
				return 0;

			if (field == 0)
				_ = mainWindow.Invoke(() => field = mainWindow.Handle);

			return field;
		}

		private set;
	}

	internal MapKeyValueIteratorData MapKeyValueIteratorData => field ??= new();
	internal OwnPropsIteratorData OwnPropsIteratorData => field ??= new();
	internal PlatformProvider PlatformProvider => field ??= new();
	internal ProcessesData ProcessesData => field ??= new();
	internal Reflections Reflections { get; private set; }
	internal ReflectionsData ReflectionsData { get; } = new();//Don't lazy initialize, it's always needed in every Script.TheScript.
	internal RegExData RegExData => field ??= new();
	internal RegExIteratorData RegExIteratorData => field ??= new();
	internal RegExIteratorDataCs RegExIteratorDataCs => field ??= new();
	internal StringsData StringsData => stringsData ??= new();
	internal ToolTipData ToolTipData => field ??= new();
	internal WindowProvider WindowProvider => field ??= new();

#if LINUX
	internal string ldLibraryPath = Environment.GetEnvironmentVariable("LD_LIBRARY_PATH") ?? "";
#endif

	static Script()
	{
		WindowX.SetProcessDPIAware();
		CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
		CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
#if LINUX
		Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);//For some reason, linux needs this for rich text to work.
		enc1252 = Encoding.GetEncoding(1252);
#endif
		SetInitialFloatFormat();//This must be done intially and not just when A_FormatFloat is referenced for the first time.
	}

	public Script()
	{
		Script.TheScript = this;//Everywhere in the script will reference this.
		timeLastInputPhysical = DateTime.UtcNow;
		timeLastInputKeyboard = timeLastInputPhysical;
		timeLastInputMouse = timeLastInputPhysical;
		threads = new(() => new());
		Vars = new Variables();
		_ = Script.TheScript.Threads.PushThreadVariables(0, true, false, true);//Ensure there is always one thread in existence for reference purposes, but do not increment the actual thread counter.
		var pd = ProcessesData;
		mgr = PlatformProvider.Manager;
		pd.MainThreadID = mgr.CurrentThreadId();
		pd.ManagedMainThreadID = Thread.CurrentThread.ManagedThreadId;//Figure out how to do this on linux.//TODO
																	  //If we're running via passing in a script and are not in a unit test, then set the working directory to that of the script file.
		var path = Path.GetFileName(Application.ExecutablePath).ToLowerInvariant();

		if (path != "testhost.exe" && path != "testhost.dll" && !A_IsCompiled)
			_ = Dir.SetWorkingDir(A_ScriptDir);

		msgFilter = new MessageFilter(this);
		Application.AddMessageFilter(msgFilter);
		_ = InitHook();//Why is this always being initialized even when there are no hooks? This is very inefficient.//TODO
					   //Init the API classes, passing in this which will be used to access their respective data objects.
		Reflections = new();
		SetInitialFloatFormat();//This must be done intially and not just when A_FormatFloat is referenced for the first time.
		tickTimer.Elapsed += TickTimerCallback;
		tickTimer.AutoReset = false;
		tickTimer.Start();
	}

	~Script()
	{
		tickTimer?.Dispose();
		Application.RemoveMessageFilter(msgFilter);
	}

	[DebuggerStepThrough]
	private void TickTimerCallback(object sender, EventArgs e) => loopShouldDoEvents = true;

	/// <summary>
	/// Will be a generated call within Main which calls into this class to add DLLs.
	/// </summary>
	/// <param name="p"></param>
	/// <param name="s"></param>
	public void LoadDll(string dll, bool throwOnFailure = true)
	{
		if (dll.Length == 0)
		{
			if (!mgr.SetDllDirectory(null))//An empty #DllLoad restores the default search order.
				if (throwOnFailure)
				{
					_ = Errors.ErrorOccurred("PlatformProvider.Manager.SetDllDirectory(null) failed.", null, Keyword_ExitApp);
					return;
				}
		}
		else if (Directory.Exists(dll))
		{
			if (!mgr.SetDllDirectory(dll))
				if (throwOnFailure)
				{
					_ = Errors.ErrorOccurred($"PlatformProvider.Manager.SetDllDirectory({dll}) failed.", null, Keyword_ExitApp);
					return;
				}
		}
		else
		{
			var dllname = dll;
#if WINDOWS

			if (!dllname.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
				dllname += ".dll";

#endif
			var hmodule = mgr.LoadLibrary(dllname);

			if (hmodule != 0)
			{
#if WINDOWS
				// "Pin" the dll so that the script cannot unload it with FreeLibrary.
				// This is done to avoid undefined behavior when DllCall optimizations
				// resolves a proc address in a dll loaded by this directive.
				_ = WindowsAPI.GetModuleHandleEx(WindowsAPI.GET_MODULE_HANDLE_EX_FLAG_PIN, dllname, out hmodule);  // MSDN regarding hmodule: "If the function fails, this parameter is NULL."
#endif
			}
			else if (throwOnFailure)
			{
				_ = Errors.ErrorOccurred($"Failed to load DLL {dllname}.", null, Keyword_ExitApp);
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
		if (!IsMainWindowClosing)
			mainWindow?.CheckedBeginInvoke(() =>
		{
			if (!IsMainWindowClosing && !AnyPersistent())
				_ = Flow.ExitAppInternal(exitReason, Environment.ExitCode, false);
		}, true, true);
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
		mainWindow.Icon = Core.Properties.Resources.Keysharp_ico;
		persistent = _persistent;
		mainWindowGui = new Gui(null, null, null, mainWindow);
		//Only do these on Windows, because it seems to have the opposite effect on linux:
		//The main window is always shown on startup, but in a broken non-drawn state.
#if WINDOWS
		mainWindow.AllowShowDisplay = false; // Prevent show on script startup
		mainWindow.ShowInTaskbar = true; // Without this the main window won't have a taskbar icon
#endif
		_ = mainWindow.BeginInvoke(() =>
		{
			var ret = Threads.BeginThread();
			// Replace configData with the prototype, which will later be used to initialize all
			// subsequent pseudo-threads. After the auto-execute thread has finished, restore the
			// original configData object because the thread variables object may be reused later.
			var prevConfigData = ret.Item2.configData;
			ret.Item2.configData = AccessorData.threadConfigDataPrototype;
			var autoExecResult = Flow.TryCatch(() =>
			{
				_ = userInit();
				//HotkeyDefinition.ManifestAllHotkeysHotstringsHooks() will be called inside of userInit() because it
				//must be done:
				//  After the window handle is created and the handle isn't valid until mainWindow.Load() is called.
				//  Also right after all hotkeys and hotstrings are created.
				IsReadyToExecute = true;
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
		Application.Run(mainWindow);
	}

	public void SetName(string s) => scriptName = s;

	public void SetReady() => IsReadyToExecute = true;

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

	[PublicForTestOnly]
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

		if (Tray != null && Tray.ContextMenuStrip != null)
		{
			Tray.ContextMenuStrip.CheckedInvoke(() =>
			{
				Tray.Visible = false;
				Tray.Dispose();
				Tray = null;
			}, true);
		}
	}

	public override string ToString() => $"Script {scriptName} {instanceCount++}";

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

		foreach (var r in ver.AsSpan().Split('.'))
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

		if (totalExistingThreads > 0)
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
		_ = HookThread.kbdMsSender;//This should always be non-null if any hotkeys/strings are present.
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

		if (Clipboard.ContainsText() || Clipboard.ContainsFileDropList())
			while (!b && i < ClipFunctions.Count)
				b = IfTest(ClipFunctions[i++].Call(1));//Can't use foreach because the collection could be modified by the event.
		else if (!KeysharpEnhancements.IsClipboardEmpty())
			while (!b && i < ClipFunctions.Count)
				b = IfTest(ClipFunctions[i++].Call(2));
		else
			while (!b && i < ClipFunctions.Count)
				b = IfTest(ClipFunctions[i++].Call(0));
	}
}