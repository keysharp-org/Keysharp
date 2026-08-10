

using Keysharp.Builtins;
#if WINDOWS
using Keysharp.Builtins.COM;
#endif

[assembly: InternalsVisibleTo("Keysharp")]
[assembly: InternalsVisibleTo("Keysharp.Tests")]
[assembly: InternalsVisibleTo("Keysharp.Benchmark")]
[assembly: InternalsVisibleTo("Keyview")]

namespace Keysharp.Runtime
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
	public partial class Script : IDisposable
	{
		internal static bool dpimodeset;//This should be done once per process, so it can be static.
#if WINDOWS
		private static int screenSystemEventsInitialized;
#endif
#if !WINDOWS
			private static Encoding enc1252 = Encoding.Default;
			private static bool etoAppConfigured;
#endif

		/// <summary>
		/// SynchronizationContext for dispatching script-logic work (timers, OnMessage, hotkeys) onto the
		/// script's logical main thread, serialized through <see cref="mainEventScheduler"/>. This is distinct
		/// from <see cref="UIThreadContext"/>, which targets whatever thread the UI framework actually requires
		/// (these can differ once window construction is lazy/headless).
		/// </summary>
		internal SynchronizationContext ScriptMainThreadContext
			=> scriptMainThreadContext ??= new ScriptEventSynchronizationContext(mainEventScheduler);

		/// <summary>
		/// SynchronizationContext for dispatching genuine UI-framework operations (Show/Hide, native dialogs,
		/// control creation) onto the thread the UI framework demands. Backed by
		/// <see cref="WindowsFormsSynchronizationContext"/>/<see cref="EtoSynchronizationContext"/>, set up by
		/// <see cref="InitializeUIThreadContext"/>.
		/// </summary>
		public SynchronizationContext UIThreadContext;

		private SynchronizationContext scriptMainThreadContext;
		private ScriptEventScheduler mainEventScheduler;

		internal readonly uint NativeMainThreadID;
		internal readonly int ManagedMainThreadID;
		internal uint NativeMainThreadId => NativeMainThreadID;

		internal bool IsOnMainThread
		{
            get {
#if !WINDOWS
				if (!IsUiInitializationBlocked)
				{
					var app = Application.Instance;
					if (app != null)
						return app.IsUIThread;
				}
#endif
			    return Environment.CurrentManagedThreadId == ManagedMainThreadID;
            }
		}

		public const string dotNetMajorVersion = "10";

		/// <summary>
		/// True when running under the NUnit/VSTest host process (<c>testhost</c>).
		/// This indicates a test-host runtime, not necessarily a user-requested headless mode.
		/// </summary>
		public static readonly bool IsTestHost = AppDomain.CurrentDomain.FriendlyName == "testhost";

		/// <summary>
		/// True when scripts should run without normal UI affordances.
		/// This is true when headless mode is forced or when no displays are available.
		/// </summary>
		public static bool IsHeadless => IsHeadlessForced()
			|| IsUiInitializationBlocked
			|| !HasAvailableDisplay();

		/// <summary>
		/// True when this host must not attempt Eto UI initialization.
		/// </summary>
		// On macOS, the AppKit event loop must run on the OS main thread (thread 1).
		// The NUnit test adapter does not run on thread 1, so we cannot drive the
		// Cocoa event loop from within testhost. Keep macOS tests headless.
		internal static bool IsUiInitializationBlocked =>
#if OSX
			IsTestHost;
#else
			false;
#endif

		private static bool HasAvailableDisplay()
		{

			try
			{
#if WINDOWS
				return System.Windows.Forms.Screen.AllScreens?.Length > 0;
#else
				// Before Eto is initialized, Screen queries may throw on some hosts.
				// Avoid incorrectly classifying normal desktop runs as headless.
					if (Eto.Forms.Application.Instance == null)
					return DefaultWithoutUILoop();

				return Eto.Forms.Screen.Screens?.Any() == true;
#endif
			}
			catch
			{
				return DefaultWithoutUILoop();
			}

			static bool DefaultWithoutUILoop()
			{
				#if WINDOWS
				return false;
#elif LINUX
				return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"))
					|| !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));
#else
				return !IsTestHost;
#endif
			}
		}

		private static bool IsHeadlessForced()
		{
			var value = Environment.GetEnvironmentVariable("KEYSHARP_FORCE_HEADLESS");
			return Options.OnOff(value) ?? false;
		}
		internal MessageFilter msgFilter;
		internal volatile bool hasExited = false;
		public bool ForceKeybdHook;
		public string[] ScriptArgs = [];
		public string[] KeysharpArgs = [];
		public uint MaxThreadsTotal = 12u;
		public bool NoMainWindow = false;
		public bool NoTrayIcon = false;
		public bool ErrorStdOut = false;   // #ErrorStdOut: write uncaught errors to stderr instead of showing a dialog

		public bool ValidateThenExit;
		public bool WinActivateForce = false;
		//Some unit tests use try..catch in non-script code, which causes ErrorOccurred to display the error dialog.
		//This allows to suppress it, but only inside ErrorOccurred (not in TryCatch etc).
		public bool SuppressErrorOccurredDialog = IsTestHost;
		//This allows to suppress all error processing
		public uint SuppressErrorOccurred = 0;
		internal const double DefaultErrorDouble = double.NaN;
		internal const int DefaultErrorInt = int.MinValue;
		internal const long DefaultErrorLong = long.MinValue;
		internal const string DefaultNewLine = "\n";
		internal static string DefaultObject => currentCompatibilityReturnsUnsetByDefault ? null : "";
		// True in v2.1+ compatibility mode, where the item/member accessors the Compatibility Mode docs list
		// (Array.__Item/Get, Map.__Item/Delete/Get, Object.GetMethod/GetOwnPropDesc, Gui.__Item) return unset (null)
		// for an absent item instead of throwing (UnsetItemError/IndexError/KeyError/etc.). This mirrors the caller's
		// mode, since built-ins carry no [CompatibilityMode] of their own.
		internal static bool CompatReturnsUnsetForMissing => currentCompatibilityReturnsUnsetByDefault;
		internal const string DefaultErrorString = "";
		internal const int INTERVAL_UNSPECIFIED = int.MinValue + 303;
		internal const int maxEmergencyThreads = 10;
		internal const int maxThreadsLimit = 0xFF;
		internal const int SLEEP_INTERVAL = 10;
		internal const int SLEEP_INTERVAL_HALF = SLEEP_INTERVAL / 2;
		internal static readonly Semver.SemVersion DefaultCompatibilityVersion = new(2, 0, 0);
		[ThreadStatic]
		private static Semver.SemVersion currentCompatibilityVersion;
		[ThreadStatic]
		private static bool currentCompatibilityReturnsUnsetByDefault;
		internal Semver.SemVersion CurrentCompatibilityVersion => currentCompatibilityVersion ?? DefaultCompatibilityVersion;
		internal CallbackRegistry<CallbackRegistration> ClipFunctions = new();
		internal List<KeysharpFunc> hotCriterions = [];
		internal List<KeysharpFunc> hotExprs = [];
		internal InputType input;
		internal int inputBeforeHotkeysCount;
		internal DateTime inputTimeoutAt = DateTime.UtcNow;
		internal bool inputTimerExists;
		internal MainWindow mainWindow;
		internal Gui mainWindowGui;
		/// <summary>
		/// Reference-counted set of menu IDs that are currently displayed. A single shared flag would let one menu
		/// flip "not visible" while another is still open (or, for the default non-blocking Show, leave it stuck set),
		/// so timers/hotkey subroutines fire during a menu or are held forever. Tracking a set keeps the menu-visible
		/// state true until the LAST menu closes and is idempotent when the same menu reports closed twice (e.g.
		/// Tsmi_Click clearing up-front and then the Closed event). The set is mutated only under <see cref="menuVisibleGate"/>
		/// — its writers run on the UI thread (and, for the Windows MenuItem.Disposed reconcile, possibly the GC
		/// finalizer thread), so the lock, not thread affinity, is what makes it safe; <see cref="menuVisibleCount"/>
		/// mirrors its size as a volatile int so the low-level keyboard-hook thread can read visibility without a
		/// lock or a torn value.
		/// </summary>
		private readonly HashSet<int> openMenuIds = [];
		private readonly object menuVisibleGate = new();
		private volatile int menuVisibleCount;

		/// <summary>
		/// AutoHotkey's g_MenuIsVisible: Popup while any menu is open, None otherwise. Kept typed as <see cref="MenuType"/>
		/// because the keyboard hook (<c>WindowsHookThread</c>) and input readers (<c>InputType</c>) compare it against
		/// <see cref="MenuType.None"/>. Reads the volatile mirror so those cross-thread reads see the UI-thread writes.
		/// </summary>
		internal MenuType menuIsVisible => menuVisibleCount != 0 ? MenuType.Popup : MenuType.None;

		/// <summary>
		/// True while at least one menu (tray, popup, or menu bar) is displayed. Mirrors AutoHotkey's g_MenuIsVisible:
		/// timers are held (see the scheduler pump) and the keyboard hook passes keystrokes through natively while set.
		/// </summary>
		internal bool IsMenuVisible => menuVisibleCount != 0;

		/// <summary>
		/// Marks <paramref name="menuId"/> as shown (<paramref name="visible"/> true) or closed. Reference-counted and
		/// idempotent per menu, so overlapping menus and the double clear from Tsmi_Click + the Closed event can't
		/// clobber each other. Writing the volatile <see cref="menuVisibleCount"/> publishes the change to the hook thread.
		/// </summary>
		internal void SetMenuVisible(int menuId, bool visible)
		{
			lock (menuVisibleGate)
			{
				if (visible)
					_ = openMenuIds.Add(menuId);
				else
					_ = openMenuIds.Remove(menuId);

				menuVisibleCount = openMenuIds.Count;
			}
		}

		internal int nMessageBoxes;
		internal CallbackRegistry<CallbackRegistration> onErrorHandlers = new();
		internal CallbackRegistry<CallbackRegistration> onExitHandlers = new();
		private Icon _normalIcon = null;
		public Icon normalIcon
		{
			get
			{
#if WINDOWS
				if (_normalIcon == null)
					_normalIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
#endif
				return _normalIcon ??= ImageHelper.IconFromByteArray(Keysharp.Internals.Properties.Resources.Keysharp_ico);
			}
		}
		private Icon _pausedIcon;
		internal Icon pausedIcon => _pausedIcon ??= ImageHelper.IconFromByteArray(Keysharp.Internals.Properties.Resources.Keysharp_p_ico);
		private Icon _suspendedIcon;
		internal Icon suspendedIcon => _suspendedIcon ??= ImageHelper.IconFromByteArray(Keysharp.Internals.Properties.Resources.Keysharp_s_ico);
		internal bool persistent;
		internal nint playbackHook = 0;
		internal DateTime priorHotkeyStartTime = DateTime.UtcNow;
		public string scriptPath = "";
		public string scriptName = "";
		internal string thisHotkeyName, priorHotkeyName;
		internal DateTime thisHotkeyStartTime;
		internal ThreadLocal<Threads> threads;
		internal readonly ThreadLocal<ModuleData> moduleData = new ();
		internal DateTime timeLastInputKeyboard;
		internal DateTime timeLastInputMouse;
		internal DateTime timeLastInputPhysical = DateTime.UtcNow;
		internal int totalExistingThreads;//Even though the thread stacks are on a per-real-thread basis, we keep a global count of threads. This may need to change in the future.
		internal long pseudoThreadSequence;
		internal int uninterruptibleTime = 17;
		private static int instanceCount;
		private AccessorData accessorData;
#if WINDOWS
		private ComMethodData comMethodData;
#endif
		private DllData dllData;
		private DriveTypeMapper driveTypeMapper;
#if !WINDOWS
		private EnvData envData;
#endif
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
		private ProcessesData processesData;
		private RegExData regExData;
		private StringsData stringsData;
		private ToolTipData toolTipData;
		private Dictionary<string, WindowGroup> windowGroups;
		private WinEventManager winEventManager;
		private MonitorEventManager monitorEventManager;
		private ClrEventManager clrEventManager;
		private int disposeStarted;

		public static Keysharp.Runtime.Script TheScript { get; internal set; }
		public Type ProgramType;
		public string ProgramNamespace = Keywords.MainNamespaceName;
		internal HotstringManager HotstringManager => hotstringManager ?? (hotstringManager = new ());
		public Threads Threads => threads.Value;
		public Variables Vars { get; private set; }
		internal AccessorData AccessorData => accessorData ?? (accessorData = new ());
#if WINDOWS
		internal ComMethodData ComMethodData => comMethodData ?? (comMethodData = new ());
#endif
		internal DllData DllData => dllData ?? (dllData = new ());
		internal DriveTypeMapper DriveTypeMapper => driveTypeMapper ?? (driveTypeMapper = new ());
#if !WINDOWS
		internal EnvData EnvData => envData ?? (envData = new ());
#endif
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
#if WINDOWS
				if (mainWindow == null)
					EnsureMainWindowHandle();
#else
				if (mainWindow == null)
					return 0;
#endif

				if (mainWindowHandle == 0)
				{
					if (mainWindow.InvokeRequired)
						_ = mainWindow.Invoke(() => mainWindowHandle = mainWindow.Handle);
					else
						mainWindowHandle = mainWindow.Handle;
				}

				return mainWindowHandle;
			}
		}

		internal IPermissionManager Permissions => Platform.Permissions;
		internal ProcessesData ProcessesData => processesData ?? (processesData = new ());
		internal Reflections Reflections { get; private set; }
		internal ReflectionsData ReflectionsData { get; } = new ();//Don't lazy initialize, it's always needed in every Script.TheScript.
		internal RegExData RegExData => regExData ?? (regExData = new ());
		internal StringsData StringsData => stringsData ?? (stringsData = new ());
		internal ToolTipData ToolTipData => toolTipData ?? (toolTipData = new ());
		/// <summary>Named window groups (GroupAdd/GroupActivate), keyed case-insensitively. Per-Script state
		/// (was <c>WindowManagerBase.Groups</c>, reached through the deleted WindowProvider).</summary>
		internal Dictionary<string, WindowGroup> WindowGroups => windowGroups ??= new (StringComparer.OrdinalIgnoreCase);

		/// <summary>Lazily-created per-script engine for <c>Ks.WinEvent</c> subscriptions; owns the platform window-event backend.</summary>
		internal WinEventManager WinEventManager => winEventManager ?? (winEventManager = new (this));

		/// <summary>The WinEvent manager if one has been created, else null (used by cleanup paths that must not create it).</summary>
		internal WinEventManager WinEventManagerIfExists => winEventManager;

		/// <summary>Lazily-created per-script engine for <c>Ks.Monitor.OnChange</c> subscriptions; owns the platform
		/// monitor-event backend, which stays uninstalled until the first subscription.</summary>
		internal MonitorEventManager MonitorEventManager => monitorEventManager ?? (monitorEventManager = new (this));

		/// <summary>The monitor-event manager if one has been created, else null (used by cleanup paths that must not create it).</summary>
		internal MonitorEventManager MonitorEventManagerIfExists => monitorEventManager;

		/// <summary>Lazily-created registry of live CLR event subscriptions made through <c>Clr</c>'s <c>OnEvent</c>.</summary>
		internal ClrEventManager ClrEventManager => clrEventManager ?? (clrEventManager = new ());

		/// <summary>The CLR event manager if one has been created, else null (used by cleanup paths that must not create it).</summary>
		internal ClrEventManager ClrEventManagerIfExists => clrEventManager;

#if OSX
		internal string ldLibraryPath = Environment.GetEnvironmentVariable("DYLD_LIBRARY_PATH") ?? "";
#elif LINUX
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
				if (e.Exception is Keysharp.Builtins.Flow.UserRequestedExitException) return; // silence during shutdown
				System.Diagnostics.Debug.Write("ThreadException caught: " + e.Exception);
			};
#endif

			AppDomain.CurrentDomain.UnhandledException += (s, e) =>
			{
				var ex = e.ExceptionObject as Exception;
				if (Script.TheScript?.hasExited == true || ex is Keysharp.Builtins.Flow.UserRequestedExitException) return; // silence during shutdown
				System.Diagnostics.Debug.WriteLine("Exception caught in current domain: " + ex);
			};

			WindowX.SetProcessDPIAware();
			CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
			CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
#if !WINDOWS
			Eto.Platform.AllowReinitialize = true;

			Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);//For some reason, linux needs this for rich text to work.
			enc1252 = Encoding.GetEncoding(1252);
#endif
			SetInitialFloatFormat();//This must be done intially and not just when A_FormatFloat is referenced for the first time.

#if WINDOWS
			// Temporary patch to override the GdiPlus initialization settings used by WinForms
			// (or more specifically System.Drawing). If StartupParameters is not 4 then external
			// codecs such as webp cannot be used.
			EnsureGdiPlus();
#endif

#if LINUX
			// Keysharp still uses X11 from multiple threads on X11 sessions (and via XWayland fallbacks), so
			// Xlib must be put into threaded mode before any display connection is opened.
			_ = Keysharp.Internals.Window.Linux.X11.Xlib.XInitThreads();

			// Resolve the platform host on the startup thread — the deterministic resolution point, before any
			// hook/IPC thread can freeze a thread-affine probe (IsX11Available reads the [ThreadStatic] XDisplay).
			// Construction is trivial; each service resolves its session/backend lazily on first use.
			_ = Platform.Instance;

			// Use GTK's native backend per session: Wayland on a Wayland session, X11 on an X11 session. We
			// no longer force GDK_BACKEND=x11 (XWayland) — window management, input synthesis and screen
			// capture all go through the compositor backends (KWin/GNOME/Cinnamon) on Wayland rather than
			// X11. A user can still pin a backend explicitly via the GDK_BACKEND environment variable.

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
			//Create the message filter before publishing TheScript. Windows belonging to a previous script (or forms
			//owned by another pumping thread) can dispatch a message the instant TheScript points at this instance,
			//and KeysharpForm.WndProc reads TheScript.msgFilter, so it must never observe a half-constructed script.
			msgFilter = new MessageFilter(this);
			Script.TheScript = this;//Everywhere in the script will reference this.
			MainWindow.ResetDebugOutputBuffer();

			NativeMainThreadID = CurrentThreadId();
			ManagedMainThreadID = Environment.CurrentManagedThreadId;

			ProgramType = program ?? GetCallingType();
			ProgramNamespace = ProgramType.Namespace;

			timeLastInputPhysical = DateTime.UtcNow;
			timeLastInputKeyboard = timeLastInputPhysical;
			timeLastInputMouse = timeLastInputPhysical;

			//Init the API classes, passing in this which will be used to access their respective data objects.
			Reflections = new Reflections();
			Vars = new Variables();
			Vars.InitClasses();

			threads = new(() => new());

			Script.TheScript.Threads.EnsureCurrentThreadVariables();

			mainEventScheduler = ThreadScheduler;

#if WINDOWS
			Application.AddMessageFilter(msgFilter);
			InitializeScreenSystemEventsOnNeutralContext();
#else
			if (!IsOnMainThread)
				PostToUIThread(msgFilter.Attach);
			else
				msgFilter.Attach();
#endif
			//Must be set BEFORE the hook thread is constructed: HookThread.KeybdMutexName/MouseMutexName are
			//instance field initializers derived from MutexName, so assigning it afterwards left them holding
			//the previous value and made #HookMutexName a no-op.
			if (hookMutexName != null && hookMutexName != "") Keysharp.Internals.Input.Hooks.HookThread.MutexName = hookMutexName;
			HookThread = CreateHookThread();
			//Init the data objects that the API classes will use.
			SetInitialFloatFormat();//This must be done intially and not just when A_FormatFloat is referenced for the first time.
		}

#if WINDOWS
		private static void InitializeScreenSystemEventsOnNeutralContext()
		{
			if (Interlocked.Exchange(ref screenSystemEventsInitialized, 1) != 0)
				return;

			var oldContext = System.ComponentModel.AsyncOperationManager.SynchronizationContext;

			try
			{
				// Screen.WorkingArea lazily subscribes to SystemEvents.UserPreferenceChanged and SystemEvents
				// captures the current SynchronizationContext for future callbacks. Use a neutral context once
				// per process so that callback is not bound to a per-script WinForms UI thread which may be
				// torn down between tests/scripts, causing Application.SetColorMode() to block forever.
				System.ComponentModel.AsyncOperationManager.SynchronizationContext = new SynchronizationContext();
				_ = System.Windows.Forms.Screen.PrimaryScreen?.WorkingArea;
			}
			catch
			{
			}
			finally
			{
				System.ComponentModel.AsyncOperationManager.SynchronizationContext = oldContext;
			}
		}
#endif

		~Script()
		{
			Dispose(false);
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

		internal long GetPeekFrequency()
		{
			var tv = threads != null && threads.IsValueCreated ? threads.Value.CurrentThread : null;
			return tv != null ? tv.configData.peekFrequency : ThreadVariables.DefaultPeekFrequency;
		}

		internal bool IsCurrentThreadPreemptiveCheckDue()
		{
			var tv = Threads.CurrentThread;
			var freq = tv.configData.peekFrequency;

			if (freq < 0L)
				return false;

			var nowTick = Environment.TickCount;
			var threshold = freq > int.MaxValue ? int.MaxValue : (int)freq;
			return unchecked((uint)(nowTick - tv.lastPeekTick)) > unchecked((uint)threshold);
		}

		internal void RecordMessageCheck() => Threads.CurrentThread.lastPeekTick = Environment.TickCount;

		/// <summary>
		/// Backs the <c>#Package</c> directive: a generated, argument-less call at the position of the script's first
		/// <c>#Package</c> line. The packages were resolved when the script was COMPILED and the exact resolution is
		/// embedded in the script's own assembly, so this only has to find that manifest and load what it names —
		/// hence <see cref="ProgramType"/>, which is the generated <c>Program</c> type and therefore identifies the
		/// assembly the manifest lives in, whether the script was run from source or precompiled.
		/// </summary>
		public void LoadPackages() => NuGetPackageLoader.Load(ProgramType?.Assembly);

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
						_ = Errors.ErrorOccurred("Platform.Library.SetDllDirectory(null) failed.", null, Keyword_ExitApp);
						return;
					}
			}
			else if (Directory.Exists(library))
			{
				if (!SetDllDirectory(library))
					if (throwOnFailure)
					{
						_ = Errors.ErrorOccurred($"Platform.Library.SetDllDirectory({library}) failed.", null, Keyword_ExitApp);
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

#if !WINDOWS
				if (hmodule == 0 && libraryName.EndsWith(Keywords.LibraryExtension, StringComparison.OrdinalIgnoreCase))
					hmodule = LoadLibrary(libraryName + ".0");
#endif

				if (hmodule != 0)
				{
#if WINDOWS
					// "Pin" the dll so that the script cannot unload it with FreeLibrary.
					// This is done to avoid undefined behavior when DllCall optimizations
					// resolves a proc address in a dll loaded by this directive.
					_ = WindowsAPI.GetModuleHandleEx(WindowsAPI.GET_MODULE_HANDLE_EX_FLAG_PIN, libraryName, out hmodule);  // MSDN regarding hmodule: "If the function fails, this parameter is NULL."
#else
					Dll.loadedDlls[library] = hmodule;
#endif
				}
				else if (throwOnFailure)
				{
					_ = Errors.ErrorOccurred($"Failed to load DLL {libraryName}.", null, Keyword_ExitApp);
					return;
				}
			}
		}

		/// <summary>
		/// Backing method for the <c>#Requires capability</c> directive: requests the listed capabilities up
		/// front and exits the app if any is denied, because the script declared it cannot run without them.
		/// <para>
		/// This lives here rather than in <c>Keysharp.Builtins</c> so that it is not part of the script API:
		/// a directive may terminate the script, but a function a script can call should return status and let
		/// the script decide. <see cref="Builtins.Ks.RequestCapabilities"/> is the script-facing equivalent and
		/// deliberately does not exit. It must stay public because the generated script assembly calls it.
		/// </para>
		/// </summary>
		/// <param name="capabilities">The capability names the directive listed.</param>
		/// <returns>An empty value once every capability is granted.</returns>
		public static object RequireCapabilities(params object[] capabilities)
		{
			var requested = Builtins.Ks.ParseRequestedCapabilities(capabilities);
			Builtins.Ks.RequestCapabilitiesBatched(requested);

			var denied = new List<string>();

			foreach (var cap in requested)
				if (!Builtins.Ks.QueryCapabilityStatus(cap).IsGranted)
					denied.Add(Builtins.Ks.CapabilityName(cap));

			if (denied.Count == 0)
				return DefaultObject;

			_ = Builtins.Ks.OutputDebugLine(
				$"Keysharp: required capability/capabilities not granted: {string.Join(", ", denied)}. Exiting. " +
				"Re-run and choose Allow (or grant it persistently) to continue.");
			return Builtins.Flow.ExitApp(1L);
		}

		public static bool HandleSingleInstance(string title, eScriptInstance inst)
		{
			if (title.Length == 0 || title == "*")//Happens when running in Keyview.
				return false;

			if (IsUiInitializationBlocked)
				return false;

			if (Env.FindCommandLineArg("force") != null || Env.FindCommandLineArg("f") != null)
				inst = eScriptInstance.Off;

			if (Env.FindCommandLineArg("restart") != null || Env.FindCommandLineArg("r") != null)
				inst = eScriptInstance.Force;

			//Restrict the search to windows belonging to this executable, otherwise an unrelated
			//app whose window title happens to match the script's filename  would be mistaken for 
			// another running instance.
			using var currentProcess = Process.GetCurrentProcess();
			title = $"{title} ahk_exe {currentProcess.ProcessName}";
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
#if !WINDOWS
						// MsgBox below needs Application.Instance, but this runs before the
						// rest of the UI is initialized -- ensure it exists first.
						_ = EnsureEtoApplication();
#endif

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

		public void ExitIfNotPersistent(Keysharp.Builtins.Flow.ExitReasons exitReason = Keysharp.Builtins.Flow.ExitReasons.Exit)
		{
			//Must use BeginInvoke() because this might be called from _ks_UserMainCode(),
			//so it needs to run after that thread has exited.
			if (!IsMainWindowClosing && totalExistingThreads == 0)
			{
				PostToUIThread(() =>
				{
					if (!IsMainWindowClosing && !AnyPersistent())
						_ = Keysharp.Internals.Flow.ExitAppInternal(exitReason, Environment.ExitCode, false);
				});
			}
		}

		public string GetPublicStaticPropertyNames()
		{
			var l1 = ReflectionsData.flatPublicStaticMethods.Keys.ToList();
			l1.AddRange(ReflectionsData.flatPublicStaticProperties.Keys);
			var hs = new HashSet<string>(l1);
			return string.Join(' ', hs);
		}

		public ModuleData ModuleData
		{
			get
			{
				if (moduleData.Value == null)
				{
					var defaultType = Vars?.DefaultModuleType;
					if (defaultType != null)
					{
						moduleData.Value = ModuleData.GetOrCreate(defaultType);
						SetCurrentCompatibilityVersion(moduleData.Value.CompatibilityVersion);
					}
				}

				return moduleData.Value;
			}
		}

		public Type CurrentModuleType
		{
			get => ModuleData?.ModuleType;
			set
			{
				if (value == null)
				{
					moduleData.Value = null;
					SetCurrentCompatibilityVersion(null);
					return;
				}
				else if (ModuleData?.ModuleType == value)
					return;

				if (!typeof(Keysharp.Runtime.Module).IsAssignableFrom(value))
					return;

				moduleData.Value = ModuleData.GetOrCreate(value);
				SetCurrentCompatibilityVersion(moduleData.Value.CompatibilityVersion);
			}
		}

		internal static bool ReturnsUnsetByDefault(Semver.SemVersion version) =>
			version?.Major > 2 || (version?.Major == 2 && version.Minor >= 1);

		internal void SetCurrentCompatibilityVersion(Semver.SemVersion version)
		{
			currentCompatibilityVersion = version ?? DefaultCompatibilityVersion;
			currentCompatibilityReturnsUnsetByDefault = ReturnsUnsetByDefault(currentCompatibilityVersion);
		}

		private void InitializeUIThreadContext()
		{
			if (UIThreadContext != null)
				return;
#if WINDOWS
			InitializeScreenSystemEventsOnNeutralContext();
			var current = SynchronizationContext.Current;

			if (current == null || current.GetType() == typeof(SynchronizationContext))
			{
				current = new WindowsFormsSynchronizationContext();
				SynchronizationContext.SetSynchronizationContext(current);
			}
			UIThreadContext = current;
#else
			var app = EnsureEtoApplication();

			app.AsyncInvoke(() => {
				var current = SynchronizationContext.Current;

				if (current == null || current.GetType() == typeof(SynchronizationContext))
				{
					current = new EtoSynchronizationContext(Application.Instance);
					SynchronizationContext.SetSynchronizationContext(current);
				}

				UIThreadContext = current;
			});
#endif
		}

		internal static void InvokeOnUIThread(Action action)
		{
			if (action == null)
				return;

			var script = TheScript;

			if (script == null || script.IsOnMainThread)
			{
				action();
				return;
			}

			if (script.UIThreadContext != null)
				script.UIThreadContext.Send(_ => action(), null);
#if !WINDOWS
			else if (Application.Instance != null)
				Application.Instance.Invoke(action);
#endif
			else
				action();
		}

		internal static T InvokeOnUIThread<T>(Func<T> action)
		{
			if (action == null)
				return default;

			var script = TheScript;

			if (script == null || script.IsOnMainThread || script.UIThreadContext == null)
			{
#if !WINDOWS
				if (script != null && !script.IsOnMainThread && Application.Instance != null)
				{
					T appResult = default;
					Application.Instance.Invoke(() => appResult = action());
					return appResult;
				}
#endif
				return action();
			}

			T uiResult = default;
			script.UIThreadContext.Send(_ => uiResult = action(), null);
			return uiResult;
		}

		internal static void PostToUIThread(Action action)
		{
			if (action == null)
				return;

			var script = TheScript;

			if (script?.UIThreadContext != null)
				script.UIThreadContext.Post(_ => action(), null);
#if !WINDOWS
			else if (Application.Instance != null)
				Application.Instance.AsyncInvoke(action);
#endif
			else
				action();
		}

#if !WINDOWS
		internal static Application EnsureEtoApplication()
		{
			var app = Application.Instance ?? new Application();

			if (app == null)
				throw new Exception("Unable to start Eto Application");

			if (etoAppConfigured)
				return app;

			app.UnhandledException += (s, e) =>
			{
				if (e.ExceptionObject is Keysharp.Builtins.Flow.UserRequestedExitException) return;
				System.Diagnostics.Debug.Write("ThreadException caught: " + e.ExceptionObject);
			};

			// Eto/GTK terminates the whole application when the last non-withdrawn top-level window closes.
			// Our main window is realized-but-hidden (withdrawn), so it isn't counted — which means a transient
			// overlay/tooltip closing can be "the last window" and quit the app even though the script is still
			// meant to run (Persistent, registered hotkeys, timers, GUIs, …). Veto that spontaneous termination
			// whenever the script is persistent, mirroring ExitIfNotPersistent's AnyPersistent() gate. A genuine
			// ExitApp sets hasExited before calling Quit(), so real exits are still allowed through.
			app.Terminating += (s, e) =>
			{
				var script = TheScript;

				if (script != null && !script.hasExited && script.AnyPersistent())
					e.Cancel = true;
			};

#if OSX
			if (app.Handler is Eto.Mac.Forms.ApplicationHandler macHandler)
				macHandler.AllowClosingMainForm = true;

			// Info.plist normally marks the app as LSUIElement (no Dock icon by default), but when
			// launched as a child process from Keyview's bundle, NSBundle.mainBundle resolves to
			// Keyview's own Info.plist (which has no LSUIElement), so AppKit defaults to showing a
			// Dock icon until the debounced policy update below runs. Set Accessory immediately and
			// synchronously to avoid that flash; RequestActivationPolicyUpdate will switch back to
			// Regular shortly after if a real window is already visible.
			MacNativeWindows.SetActivationPolicy(accessory: true);
			MacNativeWindows.RegisterWindowPolicyObservers();
			MacNativeWindows.RegisterFrontmostAppObserver();
			MacNativeWindows.RequestActivationPolicyUpdate();
#endif

#if LINUX
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

			etoAppConfigured = true;
			return app;
		}

#endif

		private void RunAutoExecSection(Func<object> userInit)
		{
			var autoExecResult = false;
			var executionResult = EventScheduler.TryExecuteThreadLaunch(0, false, false, tv =>
			{
				var prevConfigData = tv.configData;
				tv.configData = AccessorData.threadConfigDataPrototype;

				try
				{
					autoExecResult = Keysharp.Internals.Flow.TryCatch(() =>
					{
						_ = userInit();
						isReadyToExecute = true;
					});
				}
				finally
				{
					tv.configData = prevConfigData;
				}
			}, ThreadKind.Auto);

			if (executionResult != ScriptEventExecutionResult.Executed)
				return;

			if (!autoExecResult && !persistent)
			{
				// ExitApp rethrows UserRequestedExitException to unwind script threads.
				// On non-Windows platforms this method runs inside a Gtk# AsyncInvoke
				// callback, where an unhandled exception is escalated by
				// GLib.ExceptionManager and terminates the host process. Swallow the
				// expected exit signal here so a failing script reports a normal failure
				// instead of crashing the test host.
				try
				{
					_ = Keysharp.Builtins.Flow.ExitApp(1);
				}
				catch (Exception ex) when (Keysharp.Internals.Flow.TryGetException<Keysharp.Builtins.Flow.UserRequestedExitException>(ex, out _))
				{
				}
			}

			ExitIfNotPersistent();
		}

		/// <summary>
		/// Constructs the main window and wires up the bare minimum needed for it to act as a stable
		/// native handle (for hotkey registration, async message targets, etc.) -- without attaching
		/// any visible chrome (icon, tray menu, taskbar entry) and without ever calling Show().
		/// Safe to call repeatedly; only constructs once.
		/// </summary>
		private void EnsureMainWindowHandle()
		{
			if (mainWindow != null)
				return;

			mainWindow = new MainWindow();
			MainWindow.ResetDebugOutputFlush();
			mainWindowGui = new Gui(null, null, null, mainWindow);
			mainWindow.AllowShowDisplay = false;
		}

		/// <summary>
		/// Attaches the main window's visible chrome -- title, icon, tray menu, and taskbar
		/// visibility. Assumes <see cref="EnsureMainWindowHandle"/> has already run. This is the
		/// only place that decides whether the tray icon gets created; actually displaying the
		/// window is still left to the caller (e.g. ShowIfNeeded(), or the platform-specific
		/// startup sequence in <see cref="InitializeUnixMainWindow"/>/<see cref="RunMainWindow"/>).
		/// </summary>
		private void AttachMainWindowChrome(string title, bool showInTaskbar, bool initializeUiChrome)
		{
			if (!string.IsNullOrEmpty(title))
				mainWindow.Text = title;

			if (initializeUiChrome && normalIcon != null)
				mainWindow.Icon = normalIcon;

			if (initializeUiChrome && Tray == null)
				CreateTrayMenu();

			mainWindow.ShowInTaskbar = showInTaskbar;
		}

		public void RunMainWindow(string title, Func<object> userInit, bool _persistent)
		{
			if (IsUiInitializationBlocked || !HasAvailableDisplay() || IsHeadlessForced())
			{
				// Skip the native UI message loop when it cannot be driven:
				//   IsUiInitializationBlocked — macOS testhost: AppKit requires OS thread 1, which
				//     the NUnit adapter does not run on.
				//   !HasAvailableDisplay()    — no display is attached (CI, SSH without X11, etc.)
				//   IsHeadlessForced()        — KEYSHARP_FORCE_HEADLESS env var set explicitly.
				// Note: #NoTrayIcon only suppresses tray chrome, not the loop — those scripts still
				// need Application.Run for event handling.
				SuppressErrorOccurredDialog = true;
				UIThreadContext ??= SynchronizationContext.Current ?? ScriptMainThreadContext;
				RunAutoExecSection(userInit);
				return;
			}

			InitializeUIThreadContext();
			persistent = _persistent;

#if LINUX
			// Establish the compositor channel only after this Script instance is fully constructed. This used to
			// run from Script's static constructor, before TheScript and its scheduler/thread state existed; a cold
			// D-Bus connection could then enter the task-wait message pump, fault, and poison the shared bridge's
			// retry state before auto-execute resolved process-global services such as the clipboard.
			//
			// It is still early enough to hide the cold-start cost: no native main window has been created yet.
			Keysharp.Internals.Window.Linux.Wayland.WaylandSelfPositioner.Prewarm();
#endif

#if WINDOWS
			EnsureMainWindowHandle();
			AttachMainWindowChrome(title, true, true);
			_ = mainWindow.BeginInvoke(() => RunAutoExecSection(userInit));
			Application.Run(mainWindow);
#else
			var app = EnsureEtoApplication();
#if LINUX
			Keysharp.Internals.Window.Linux.X11Server.InstallTestLoopXErrorHandler();
#endif

			app.AsyncInvoke(() => InitializeUnixMainWindow(app, title, userInit, _persistent));

			app.Run();
#endif
		}

#if !WINDOWS
		private void InitializeUnixMainWindow(Eto.Forms.Application app, string title, Func<object> userInit, bool persistentState)
		{
			EnsureMainWindowHandle();
			AttachMainWindowChrome(title, true, !NoTrayIcon);
			persistent = persistentState;
			mainWindow.Closed += (_, __) =>
			{
				if (hasExited)
					app.Quit();
			};
			// AllowShowDisplay is false at this point either way (set in EnsureMainWindowHandle), so
			// a Show()/Hide() round trip here would just self-hide again right away -- but even a
			// brief Show() registers a real on-screen window with the window server, which makes its
			// taskbar/Dock icon flash and can steal focus. Realize the native handle without mapping
			// the window, and leave the first visible Show() to an explicit user request.
			mainWindow.InitializeHidden();

			app.AsyncInvoke(() => RunAutoExecSection(userInit));
		}
#endif

		public void SetName(string path, string name = null)
        {
			// path is "*" for a from-stdin compile and null for a compiled file (the compiler no longer bakes the
			// absolute path in); in the latter case prefer the launcher-supplied runtime path so A_ScriptFullPath /
			// A_ScriptDir reflect where this script is actually running, falling back to the host exe path.
			scriptPath = path ?? CompilerHelper.runScriptPath ?? Accessors.A_AhkPath;

			if (name != null)
				scriptName = name;
			else
			{
				scriptName = Path.GetFileName(scriptPath);

				if (string.IsNullOrEmpty(scriptName))
					scriptName = scriptPath;
			}

			//If we're running via passing in a script and are not in a unit test, then set the working directory to that of the script file.
			var processName = Path.GetFileName(
#if WINDOWS
				Application.ExecutablePath
#else
				Environment.ProcessPath ?? string.Empty
#endif
				).ToLowerInvariant();

			if (!IsTestHost && processName != "testhost.exe" && processName != "testhost.dll" && processName != "testhost" && !A_IsCompiled)
				_ = Dir.SetWorkingDir(A_ScriptDir);
        }

		public void SetReady() => isReadyToExecute = true;

		public static void ProcessUnhandledException(Script script, Exception ex)
		{
			if (ex == null)
				return;

			var unwrapped = Keysharp.Runtime.Flow.UnwrapException(ex);

			if (unwrapped is Keysharp.Builtins.Flow.UserRequestedExitException)
				return;

			if (unwrapped is KeysharpException kserr)
			{
				var msg = "Uncaught Keysharp exception:\r\n" + kserr;
				WriteUncaughtErrorToStdErr(msg);

				if (script == null || !script.SuppressErrorOccurredDialog)
					_ = ErrorDialog.Show(kserr, false);

				return;
			}

			WriteUncaughtErrorToStdErr("Uncaught exception:\r\n" + unwrapped);

			if (script == null || !script.SuppressErrorOccurredDialog)
				_ = ErrorDialog.Show(unwrapped, false);
		}

		public static void TryProcessUnhandledException(Script script, Exception ex)
		{
			try
			{
				ProcessUnhandledException(script, ex);
			}
			catch (Exception)
			{
			}
		}

		public static void TryProcessKeysharpException(Script script, KeysharpException kserr)
		{
			if (kserr == null)
				return;

			if (!kserr.UserError.Processed)
			{
				try
				{
					_ = Errors.ErrorOccurred(kserr.UserError, kserr.UserError.ExcType);
				}
				catch (Exception)
				{
				}
			}

			if (!kserr.UserError.Handled)
				TryProcessUnhandledException(script, kserr);
		}

		public static void SafeExit(int code)
		{
			Environment.ExitCode = code;

			try
			{
				_ = Keysharp.Builtins.Flow.ExitApp(code);
			}
			catch (Exception)
			{
			}
		}

		public void HandleUncaughtException(Exception ex) => ProcessUnhandledException(this, ex);

		/// <summary>
		/// Writes uncaught script errors to stderr in debug builds to improve test diagnostics.
		/// </summary>
		/// <param name="text">The text to write.</param>
		[Conditional("DEBUG")]
		[PublicHiddenFromUser]
		internal static void WriteUncaughtErrorToStdErr(string text)
		{
			if (!string.IsNullOrEmpty(text))
				Console.Error.WriteLine(text);
		}

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		protected virtual void Dispose(bool disposing)
		{
			if (Interlocked.Exchange(ref disposeStarted, 1) != 0)
				return;

			//Everything below is managed teardown that can block on, or marshal to, another thread:
			//HookThread.Stop() joins the hook's STA thread, and winEventManager/flowData post to the UI
			//thread. None of it may run on the GC finalizer thread -- joining there stalls the whole
			//finalizer queue, which is precisely what ExitAppInternal's GC.WaitForPendingFinalizers()
			//then waits on. A Script that reaches its finalizer was never disposed (i.e. it leaked); its
			//hook thread is a background thread that dies with the process, and StringsData frees its
			//GCHandles from its own finalizer, so skipping this on that path leaks nothing that matters.
			if (!disposing)
				return;

			HookThread?.Stop();
			winEventManager?.Dispose();
			// Same reasoning as clrEventManager below on Windows: the monitor backend hangs off the *static*
			// SystemEvents.DisplaySettingsChanged, so it has to be detached explicitly or it roots this Script.
			monitorEventManager?.Dispose();
			// Before anything else managed goes away: a subscription to a *static* CLR event is a root the runtime
			// holds indefinitely, so leaving one attached keeps the callback -- and the engine behind it -- alive past
			// dispose. This is the orphaned-callback case teardown has to cover.
			clrEventManager?.Dispose();
#if LINUX
			Keysharp.Internals.Input.Linux.KeysharpInputdManager.DisconnectClients();
#endif
			stringsData?.Free();
			flowData?.Dispose();

			// Frees every overlay this process still owns (Highlight/ToolTip/Overlay builtins all register as
			// image overlays), so a script that exits without disposing them doesn't leak on-screen surfaces.
			try { _ = Platform.Overlay.TryHideAllImageOverlays(); } catch { }

			// Stops anything SoundPlay left playing. On Windows an MCI item left open can hang the process on
			// exit (AHK closes it from its destructor for the same reason); elsewhere this reaps the player.
			try { Keysharp.Internals.Os.SoundPlayback.StopCurrent(); } catch { }

#if WINDOWS
			Application.RemoveMessageFilter(msgFilter);
#elif !WINDOWS
			msgFilter?.Detach();
#endif

			if (Tray != null)
			{
				InvokeOnUIThread(DisposeTrayIcon);
			}

			if (!IsMainWindowClosing)
			{
				var window = mainWindow;
				window?.CheckedInvoke(() =>
				{
					window.Close();
					mainWindow = null;
				}, false);
			}

		}

		private void DisposeTrayIcon()
		{
			var tray = Tray;

			if (tray == null)
				return;

			try
			{
				tray.MouseClick -= TrayIcon_MouseClick;
				tray.MouseDoubleClick -= TrayIcon_MouseDoubleClick;
				tray.Tag = null;
				tray.Dispose();
			}
			catch
			{
			}
			finally
			{
				Tray = null;
				trayMenu = null;
			}
		}

		public override string ToString()
		{
			return $"Script {scriptPath} {instanceCount++}";
		}

		public static void VerifyVersion(string ver, bool reqAhk, int line, string code)
		{
			var requirement = CompatibilityVersions.NormalizeRequirement(ver, out var hasOp);
			var cmp = Strings.VerCompare(A_AhkVersion, requirement);
			var ok = hasOp ? cmp == 1L : cmp >= 0L;

			if (ok)
				return;

			if (reqAhk)
				throw new ParseException($"This script requires AutoHotkey {ver}, but Keysharp supports AutoHotkey v{A_AhkVersion}", line, code);

			throw new ParseException($"This script requires Keysharp {ver}, but you have v{A_AhkVersion}", line, code);
		}

		public void WaitThreads()
		{
			//Check against 1 instead of 0, because this may be launched in a thread as a result of a hotkey.
			//If this gets stuck in a loop it means we have a thread imbalance/mismatch somewhere.
			//We added them, but never removed. While seemingly dangerous to have, it's a handy
			//way to know we've found a bug.
			while (totalExistingThreads > 1)
				Keysharp.Internals.Flow.Sleep(200);
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

			if (HotkeyData.shk.Length > 0)
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
				RecordMessageCheck();
				return ResultType.Ok;
			}

		internal void SetHotNamesAndTimes(string name)
		{
			// Just prior to launching the hotkey, update these values to support built-in
			// variables such as A_TimeSincePriorHotkey:
			priorHotkeyName = thisHotkeyName;
			priorHotkeyStartTime = thisHotkeyStartTime;
			// Unlike hotkeys -- which can have a name independent of their label by being created or updated
			// with the HOTKEY command -- a hot string's unique name is always its label since that includes
			// the options that distinguish between (for example) :c:ahk:: and ::ahk::
			thisHotkeyName = name;
			thisHotkeyStartTime = DateTime.UtcNow; // Fixed for v1.0.35.10 to not happen for GUI
		}

		private static HookThread CreateHookThread()
		{
#if WINDOWS
			return new WindowsHookThread();
#elif LINUX
			return new LinuxHookThread();
#elif OSX
			return new MacHookThread();
#else
#error Unsupported platform. Only WINDOWS, LINUX, and OSX are supported.
#endif
		}

		private void PrivateClipboardUpdate(params object[] o)
		{
			// Just dispatch to the OnClipboardChange handlers and discard the aggregate result. It must NOT be
			// wrapped in IfTest/ForceBool: InvokeEventHandlers returns null when a handler is deferred (e.g. a
			// re-entrant A_Clipboard:= from a hotkey thread) or returns nothing, and ForceBool(null) throws
			// "input was unset". The other InvokeEventHandlers call sites already just discard the result.
			// The resolved clipboard backend reports the event type (0 = empty, 1 = text, 2 = other).
			_ = ClipFunctions.InvokeEventHandlers(Platform.Clipboard.ChangeType());
		}

		internal Type GetNativeType(Any obj)
		{
				while (obj != null)
				{
					var t = obj.type;
				if (!string.Equals(t.Namespace, ProgramNamespace, StringComparison.OrdinalIgnoreCase))
				{
					// we found a built-in prototype object
					if (t == typeof(Class)) return typeof(KeysharpObject);
					return t;
				}

					// follow the “base” link:
					obj = obj.Base;
				}
				// fallback?
				return typeof(Any);
		}

		internal void UpdateClipboardMonitoring()
		{
			var window = mainWindow;

			if (window == null)
				return;

			var enabled = ClipFunctions.Count > 0;
			PostToUIThread(() =>
			{
				if (window.IsClosing)
					return;

				window.ClipboardUpdate -= PrivateClipboardUpdate;

				if (enabled)
					window.ClipboardUpdate += PrivateClipboardUpdate;

#if !WINDOWS
				window.SetClipboardMonitoringEnabled(enabled);
#endif
			});
		}
	}
}
