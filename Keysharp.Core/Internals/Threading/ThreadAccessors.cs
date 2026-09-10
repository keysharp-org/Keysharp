using Keysharp.Builtins;

namespace Keysharp.Internals.Threading
{
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
			get
			{
				var tv = Script.TheScript.Threads.CurrentThread;
				var ei = tv.eventInfo;

				// A_EventInfo may park a Func<object> (hook events, PCRE callouts) that builds its value on first
				// read; resolve it once and cache the result back so later reads are cheap. tv.eventInfo is only
				// touched by its owning thread, so this write-back needs no synchronisation.
				if (ei is Func<object> factory)
					tv.eventInfo = ei = factory();

				return ei;
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
