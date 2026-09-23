using Keysharp.Builtins;
namespace Keysharp.Internals.Threading
{
	/// <summary>
	/// What launched a pseudo-thread, exposed to scripts as <c>Thread.Kind</c>. Set at the launch site, so a value
	/// exists only where the site can name it truthfully: every registered event handler (GUI events, menu items,
	/// OnExit, OnClipboardChange, COM value events) dispatches through one shared registry and therefore shares
	/// <see cref="Event"/>, rather than this enum carrying names nothing ever produces.
	/// </summary>
	internal enum ThreadKind : byte
	{
		None = 0,
		Auto,        // the auto-execute section
		Hotkey,
		Hotstring,
		Timer,
		Event,       // a registered handler: GUI/menu/OnExit/OnClipboardChange/ComValue
		Message,     // OnMessage
		Callback,    // a CallbackCreate pointer invoked from native code
		Input,       // InputHook
		WinEvent,
		Com,         // a COM event sink
		Clr,         // a CLR event subscription
		RealThread,  // a RealThread body, or work posted/sent to one
	}

	public class ThreadConfigData
	{
		public ThreadConfigData() { }

		internal long controlDelay = 20L;
		internal CoordModeType coordModeCaret = CoordModeType.Client;
		internal CoordModeType coordModeMenu = CoordModeType.Client;
		internal CoordModeType coordModeMouse = CoordModeType.Client;
		internal CoordModeType coordModePixel = CoordModeType.Client;
		internal CoordModeType coordModeToolTip = CoordModeType.Client;
		internal long defaultMouseSpeed = 2L;
		internal bool detectHiddenText = true;
		internal bool detectHiddenWindows;
		internal Encoding fileEncoding = Encoding.Default;
		internal long keyDelay = 10L;
		internal long keyDelayPlay = -1L;
		internal long keyDuration = -1L;
		internal long keyDurationPlay = -1L;
		internal long mouseDelay = 10L;
		internal long mouseDelayPlay = -1L;
		internal long peekFrequency = 5L;
		internal bool allowTimers = true;
		internal bool defaultIsCritical;
#if WINDOWS
		internal long regView = 64L;
#endif
		internal long sendLevel;
		internal SendModes sendMode = SendModes.Input;
		internal bool storeCapsLockMode = true;
		internal long titleMatchMode = 2L;
		internal bool titleMatchModeSpeed = true;
		internal long winDelay = 100L;

		public ThreadConfigData Clone() => (ThreadConfigData)MemberwiseClone();

		internal void CopyFromPrototypeConfigData(Script script)
		{
			var protoConfigData = script.AccessorData.threadConfigDataPrototype;
			controlDelay = protoConfigData.controlDelay;
			coordModeCaret = protoConfigData.coordModeCaret;
			coordModeMenu = protoConfigData.coordModeMenu;
			coordModeMouse = protoConfigData.coordModeMouse;
			coordModePixel = protoConfigData.coordModePixel;
			coordModeToolTip = protoConfigData.coordModeToolTip;
			defaultMouseSpeed = protoConfigData.defaultMouseSpeed;
			detectHiddenText = protoConfigData.detectHiddenText;
			detectHiddenWindows = protoConfigData.detectHiddenWindows;
			fileEncoding = protoConfigData.fileEncoding;
			keyDelay = protoConfigData.keyDelay;
			keyDelayPlay = protoConfigData.keyDelayPlay;
			keyDuration = protoConfigData.keyDuration;
			keyDurationPlay = protoConfigData.keyDurationPlay;
			mouseDelay = protoConfigData.mouseDelay;
			mouseDelayPlay = protoConfigData.mouseDelayPlay;
			peekFrequency = protoConfigData.peekFrequency;
			allowTimers = protoConfigData.allowTimers;
			defaultIsCritical = protoConfigData.defaultIsCritical;
#if WINDOWS
			regView = protoConfigData.regView;
#endif
			sendLevel = protoConfigData.sendLevel;
			sendMode = protoConfigData.sendMode;
			storeCapsLockMode = protoConfigData.storeCapsLockMode;
			titleMatchMode = protoConfigData.titleMatchMode;
			titleMatchModeSpeed = protoConfigData.titleMatchModeSpeed;
			winDelay = protoConfigData.winDelay;
		}
	}
	public class ThreadVariables
	{
		internal static readonly long DefaultPeekFrequency = 5L;
		internal static readonly long DefaultUninterruptiblePeekFrequency = 16L;
		internal readonly Script owner;

		// These describe the runtime state of the pseudo-thread
		//internal Task<object> task = null;
		internal bool task = false;
		internal bool isCritical = false;
		internal bool isPaused = false;
		internal bool allowThreadToBeInterrupted = true;
		internal int UninterruptibleDuration = 17;
		internal long threadStartTick;
		internal ScriptTimerState currentTimer;
		internal string defaultGui;
		internal Form dialogOwner;
		// A_EventInfo backing store. Holds either the final script-visible value, or a Func<object> that
		// builds it on first read (resolved and cached back by ThreadAccessors.A_EventInfo). A_EventInfo is
		// rarely read, so parking a factory lets producers (hook events, PCRE callouts) skip constructing the
		// value unless the script actually inspects it. Use SetEventInfo to park a lazy value.
		internal object eventInfo;
		internal Keysharp.Runtime.FuncScope executionScope;
		// A pseudo-thread owns its flow state, so an interrupt starts outside the interrupted thread's loops and catches.
		internal readonly Stack<Keysharp.Runtime.LoopInfo> loopStack = new ();
		// A try, or a built-in standing in for one, catches what this pseudo-thread raises (AHK's EXCPTMODE_CATCH).
		internal bool insideTry;
		// The exception the innermost active catch handles, which a bare throw re-raises.
		internal Keysharp.Builtins.KeysharpException caughtException;
		// A_Index while no loop runs, which a script may assign.
		internal long indexOutsideLoops;
		internal object hotCriterion;
		internal long hwndLastUsed = 0;
		internal long lastFoundForm;
		private Random randomGenerator;
		private StringBuilder regsb = null;
		internal long priority;
		internal int lastPeekTick;
		internal int threadId;
		internal long pseudoThreadId;
		internal int? requestedExitCode;
		internal int lastError = 0;
		// What launched this pseudo-thread; script-visible as Thread.Kind. Set once at push time.
		internal ThreadKind kind;
		// The script-visible KeysharpThread wrapper for this pseudo-thread, created on the first read of A_Thread or when
		// Exit unwinds the thread, and cleared on reuse. The wrapper captures pseudoThreadId and validates it on every
		// access, so a wrapper held past this slot's reuse reports itself inactive rather than describing a later thread.
		internal Keysharp.Builtins.KeysharpThread threadObject;

		// These describe the configuration defaults of the pseudo-thread,
		// inherited from (and set by) the auto-execute section thread
		internal ThreadConfigData configData = new ();

		internal Random RandomGenerator
		{
			get => randomGenerator != null ? randomGenerator : randomGenerator = new Random((int)(DateTime.UtcNow.Ticks & 0xFFFFFFFF));
			set => randomGenerator = value;
		}

		internal StringBuilder RegSb => regsb != null ? regsb : regsb = new StringBuilder(1024);

		internal ThreadVariables(Script owner) => this.owner = owner;

		// Every newly launched thread is uninterruptible for a startup window (the "Thread Interrupt" time) unless
		// that time is 0; a Critical thread stays uninterruptible indefinitely (-1). The becoming-interruptible moment
		// is locked in at launch so a later Thread('Interrupt', n) only affects FUTURE threads. See IsInterruptible().
		internal void ApplyUninterruptibleStartupWindow()
		{
			if (owner.uninterruptibleTime != 0 || isCritical)
			{
				allowThreadToBeInterrupted = false;

				if (isCritical || owner.uninterruptibleTime < 0)
					UninterruptibleDuration = -1;
				else
					// threadStartTick was already stamped by Init(); the uninterruptible window measures from the
					// same launch instant, so there is nothing to re-read here.
					UninterruptibleDuration = owner.uninterruptibleTime;
			}
		}

		internal void Init()
		{
			task = false;// null;
			isCritical = false;
			isPaused = false;
			allowThreadToBeInterrupted = true;
			UninterruptibleDuration = owner.uninterruptibleTime;
			// Stamped unconditionally (not just when the uninterruptible window applies) so KeysharpThread.Elapsed
			// always has a launch instant to measure from, and ApplyUninterruptibleStartupWindow reuses it.
			threadStartTick = Environment.TickCount64;
			currentTimer = null;
			defaultGui = null;
			dialogOwner = null;
			eventInfo = null;
			executionScope = null;
			loopStack.Clear();
			insideTry = false;
			caughtException = null;
			indexOutsideLoops = 0;
			hotCriterion = null;
			hwndLastUsed = 0;
			lastFoundForm = 0;
			randomGenerator = null;
			_ = (regsb?.Clear());
			priority = 0L;
			lastPeekTick = Environment.TickCount;
			threadId = 0;
			pseudoThreadId = 0L;
			requestedExitCode = null;
			lastError = 0;
			kind = ThreadKind.None;
			threadObject = null;
			// Instead of cloning the instance, copy the data because
			// allocating the memory for new instances is expensive
			configData.CopyFromPrototypeConfigData(owner);
			isCritical = configData.defaultIsCritical;
		}

		/// <summary>
		/// Parks a lazily-built value in A_EventInfo. <paramref name="factory"/> is invoked at most once,
		/// the first time the script reads A_EventInfo, and not at all if it never does.
		/// </summary>
		internal void SetEventInfo(Func<object> factory) => eventInfo = factory;
	}
}
