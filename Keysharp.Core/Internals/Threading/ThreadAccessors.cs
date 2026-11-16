using Keysharp.Builtins;

namespace Keysharp.Internals.Threading
{
	internal static class ThreadAccessors
	{
		/// <summary>
		/// Gets the current pseudo-thread's coordinate mode from its innermost scope or baseline configuration.
		/// </summary>
		internal static CoordModeType GetCoordMode(CoordMode category)
		{
			var tv = Script.TheScript.Threads.CurrentThread;
			var scope = tv.CurrentCoordWindow;
			var mode = scope != null ? scope.CoordModeFor(category)
				: tv.configData.CoordModeFor(category);
			GC.KeepAlive(scope);
			return mode;
		}

		internal static CoordModeType SetCoordMode(CoordMode category, CoordModeType value)
		{
			var tv = Script.TheScript.Threads.CurrentThread;
			var scope = tv.CurrentCoordWindow;
			ref var mode = ref (scope != null
				? ref scope.CoordModeFor(category)
				: ref tv.configData.CoordModeFor(category));
			var previous = mode;
			mode = value;

			GC.KeepAlive(scope);
			return previous;
		}

		/// <summary>
		/// The coordinate mode for positioning the caret.
		/// </summary>
		public static CoordModeType A_CoordModeCaret
		{
			get => GetCoordMode(CoordMode.Caret);
			set => SetCoordMode(CoordMode.Caret, value);
		}

		/// <summary>
		/// The coordinate mode for positioning menus.
		/// </summary>
		public static CoordModeType A_CoordModeMenu
		{
			get => GetCoordMode(CoordMode.Menu);
			set => SetCoordMode(CoordMode.Menu, value);
		}

		/// <summary>
		/// The coordinate mode for positioning the mouse.
		/// </summary>
		public static CoordModeType A_CoordModeMouse
		{
			get => GetCoordMode(CoordMode.Mouse);
			set => SetCoordMode(CoordMode.Mouse, value);
		}

		/// <summary>
		/// The coordinate mode for positioning pixels.
		/// </summary>
		public static CoordModeType A_CoordModePixel
		{
			get => GetCoordMode(CoordMode.Pixel);
			set => SetCoordMode(CoordMode.Pixel, value);
		}

		/// <summary>
		/// The coordinate mode for positioning tooltips.
		/// </summary>
		public static CoordModeType A_CoordModeToolTip
		{
			get => GetCoordMode(CoordMode.Tooltip);
			set => SetCoordMode(CoordMode.Tooltip, value);
		}

		/// <summary>
		/// The type of mouse cursor currently being displayed. It will be one of the following words:<br/>
		/// AppStarting, Arrow, Cross, Help, IBeam, Icon, No, Size, SizeAll, SizeNESW, SizeNS, SizeNWSE, SizeWE, UpArrow, Wait, Unknown.<br/>
		/// The acronyms used with the size-type cursors are compass directions, e.g. NESW = NorthEast+SouthWest.<br/>
		/// The hand-shaped cursors (pointing and grabbing) are classified as Unknown.
		/// </summary>
		public static string A_Cursor
		{
			get => GetCursor();
			set => SetCursor(value);
		}

		/// <summary>
		/// See <see cref="A_MDay"/>.
		/// </summary>
		public static string A_DD => A_MDay;

		/// <summary>
		/// Current day of the week's 3-letter abbreviation in the current user's language, e.g. Sun.
		/// </summary>
		public static string A_DDD => DateTime.Now.ToString("ddd", CaseCompare.UserCulture ?? CultureInfo.CurrentCulture);

		/// <summary>
		/// Current day of the week's full name in the current user's language, e.g. Sunday.
		/// </summary>
		public static string A_DDDD => DateTime.Now.ToString("dddd", CaseCompare.UserCulture ?? CultureInfo.CurrentCulture);

		/// <summary>
		/// True if the thread immediately underneath the current thread is paused, otherwise false.
		/// </summary>
		public static bool A_IsPaused
		{
			get => Script.TheScript.Threads.UnderlyingThread?.IsPaused ?? false;
			set
			{
				var underlyingThread = Script.TheScript.Threads.UnderlyingThread;
				if (underlyingThread != null) underlyingThread.IsPaused = value;
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
			get
			{
				var tv = Script.TheScript.Threads.CurrentThread;
				var ei = tv.eventInfo;

				// A_EventInfo may park a Func<object> (hook events, PCRE callouts) or a timer lifetime that builds its
				// value on first read; resolve it once and cache the result back so later reads are cheap. tv.eventInfo
				// is only touched by its owning thread, so this write-back needs no synchronisation.
				if (ei is Func<object> factory)
					tv.eventInfo = ei = factory();
				else if (ei is ScriptTimerInfo info)
					tv.eventInfo = ei = Ks.Timer.Bind(info);

				return ei ?? 0L;
			}
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
		/// The configured file encoding. Null represents CP0.
		/// </summary>
		internal static Encoding A_FileEncoding
		{
			get => Script.TheScript.Threads.CurrentThread.configData.fileEncoding;
			set => Script.TheScript.Threads.CurrentThread.configData.fileEncoding = value;
		}

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
		internal static long A_PeekFrequency
		{
			get => Script.TheScript.Threads.CurrentThread.configData.peekFrequency;
			set
			{
				var script = Script.TheScript;
				script.Threads.CurrentThread.configData.peekFrequency = value;
				script.RecordMessageCheck();
			}
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
