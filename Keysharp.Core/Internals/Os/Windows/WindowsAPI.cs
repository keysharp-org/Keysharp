using Keysharp.Builtins;
#if WINDOWS

using System.Runtime.InteropServices.Marshalling;

namespace Keysharp.Internals.Os.Windows
{
	internal enum GetAncestorFlags
	{
		/// <summary>
		/// Retrieves the parent window. This does not include the owner, as it does with the GetParent function.
		/// </summary>
		GetParent = 1,

		/// <summary>
		/// Retrieves the root window by walking the chain of parent windows.
		/// </summary>
		GetRoot = 2,

		/// <summary>
		/// Retrieves the owned root window by walking the chain of parent and owner windows returned by GetParent.
		/// </summary>
		GetRootOwner = 3
	}

	[Flags]
	internal enum KnownFolderFlag : uint
	{
		None = 0x0,
		CREATE = 0x8000,
		DONT_VERFIY = 0x4000,
		DONT_UNEXPAND = 0x2000,
		NO_ALIAS = 0x1000,
		INIT = 0x800,
		DEFAULT_PATH = 0x400,
		NOT_PARENT_RELATIVE = 0x200,
		SIMPLE_IDLIST = 0x100,
		ALIAS_ONLY = 0x80000000
	}

	[StructLayout(LayoutKind.Sequential)]
	internal struct NMHDR
	{
		internal nint hwndFrom;
		internal nint idFrom;
		internal uint code;
	}

	[StructLayout(LayoutKind.Sequential)]
	internal struct LVITEM
	{
		internal uint mask;
		internal int iItem;
		internal int iSubItem;
		internal uint state;
		internal uint stateMask;
		internal nint pszText;
		internal int cchTextMax;
		internal int iImage;
		internal nint lParam;
	}

	[StructLayout(LayoutKind.Sequential)]
	internal struct JOYCAPS
	{
		internal ushort wMid;
		internal ushort wPid;
		internal string szPname;
		internal uint wXmin;
		internal uint wXmax;
		internal uint wYmin;
		internal uint wYmax;
		internal uint wZmin;
		internal uint wZmax;
		internal uint wNumButtons;
		internal uint wPeriodMin;
		internal uint wPeriodMax;

		internal uint wRmin;               /* minimum r position value */
		internal uint wRmax;               /* maximum r position value */
		internal uint wUmin;               /* minimum u (5th axis) position value */
		internal uint wUmax;               /* maximum u (5th axis) position value */
		internal uint wVmin;               /* minimum v (6th axis) position value */
		internal uint wVmax;               /* maximum v (6th axis) position value */
		internal uint wCaps;               /* joystick capabilites */
		internal uint wMaxAxes;            /* maximum number of axes supported */
		internal uint wNumAxes;            /* number of axes in use */
		internal uint wMaxButtons;         /* maximum number of buttons supported */
		private readonly string szRegKey;/* registry key */
		private readonly string szOEMVxD; /* OEM VxD in use */
	}

	[Serializable, StructLayout(LayoutKind.Sequential)]
	internal struct JOYINFOEX
	{
		internal static JOYINFOEX Default
		{
			get
			{
				var jie = new JOYINFOEX();
				jie.dwSize = (uint)Marshal.SizeOf(jie);
				return jie;
			}
		}

		internal uint dwSize;                /* size of structure */
		internal uint dwFlags;               /* flags to indicate what to return */
		internal uint dwXpos;                /* x position */
		internal uint dwYpos;                /* y position */
		internal uint dwZpos;                /* z position */
		internal uint dwRpos;                /* rudder/4th axis position */
		internal uint dwUpos;                /* 5th axis position */
		internal uint dwVpos;                /* 6th axis position */
		internal uint dwButtons;             /* button states */
		internal uint dwButtonNumber;        /* current button number pressed */
		internal int dwPOV;                 /* point of view state */
		internal uint dwReserved1;           /* reserved for communication between winmm & driver */
		internal uint dwReserved2;           /* reserved for future expansion */
	}

	//This abbreviated definition is based on the actual definition in kbd.h (Windows DDK).
	[StructLayout(LayoutKind.Sequential)]
	internal struct KBDTABLES64
	{
		internal ulong pCharModifiers;
		internal ulong pVkToWcharTable;
		internal ulong pDeadKey;
		internal ulong pKeyNames;
		internal ulong pKeyNamesExt;
		internal ulong pKeyNamesDead;
		internal ulong pusVSCtoVK;
		internal byte bMaxVSCtoVK;
		internal ulong pVSCtoVK_E0;
		internal ulong pVSCtoVK_E1;

		// This is the one we want:
		internal uint fLocaleFlags;

		// Struct definition truncated.
	}

	internal enum DWMWINDOWATTRIBUTE
	{
		DWMWA_NCRENDERING_ENABLED = 1,
		DWMWA_NCRENDERING_POLICY,
		DWMWA_TRANSITIONS_FORCEDISABLED,
		DWMWA_ALLOW_NCPAINT,
		DWMWA_CAPTION_BUTTON_BOUNDS,
		DWMWA_NONCLIENT_RTL_LAYOUT,
		DWMWA_FORCE_ICONIC_REPRESENTATION,
		DWMWA_FLIP3D_POLICY,
		DWMWA_EXTENDED_FRAME_BOUNDS,
		DWMWA_HAS_ICONIC_BITMAP,
		DWMWA_DISALLOW_PEEK,
		DWMWA_EXCLUDED_FROM_PEEK,
		DWMWA_CLOAK,
		DWMWA_CLOAKED,
		DWMWA_FREEZE_REPRESENTATION,
		DWMWA_LAST
	};

	internal enum AccessProtectionFlags
	{
		PAGE_NOACCESS = 0x001,
		PAGE_READONLY = 0x002,
		PAGE_READWRITE = 0x004,
		PAGE_WRITECOPY = 0x008,
		PAGE_EXECUTE = 0x010,
		PAGE_EXECUTE_READ = 0x020,
		PAGE_EXECUTE_READWRITE = 0x040,
		PAGE_EXECUTE_WRITECOPY = 0x080,
		PAGE_GUARD = 0x100,
		PAGE_NOCACHE = 0x200,
		PAGE_WRITECOMBINE = 0x400
	}

	[Flags]
	internal enum VirtualAllocExTypes
	{
		WRITE_WATCH_FLAG_RESET = 0x00000001, // Win98 only
		MEM_COMMIT = 0x00001000,
		MEM_RESERVE = 0x00002000,
		MEM_COMMIT_OR_RESERVE = 0x00003000,
		MEM_DECOMMIT = 0x00004000,
		MEM_RELEASE = 0x00008000,
		MEM_FREE = 0x00010000,
		MEM_internal = 0x00020000,
		MEM_MAPPED = 0x00040000,
		MEM_RESET = 0x00080000, // Win2K only
		MEM_TOP_DOWN = 0x00100000,
		MEM_WRITE_WATCH = 0x00200000, // Win98 only
		MEM_PHYSICAL = 0x00400000, // Win2K only
		SEC_IMAGE = 0x01000000,
		MEM_LARGE_PAGES = 0x20000000,
		MEM_IMAGE = SEC_IMAGE
	}

	internal enum ProcessAccessTypes
	{
		PROCESS_TERMINATE = 0x00000001,
		PROCESS_CREATE_THREAD = 0x00000002,
		PROCESS_SET_SESSIONID = 0x00000004,
		PROCESS_VM_OPERATION = 0x00000008,
		PROCESS_VM_READ = 0x00000010,
		PROCESS_VM_WRITE = 0x00000020,
		PROCESS_DUP_HANDLE = 0x00000040,
		PROCESS_CREATE_PROCESS = 0x00000080,
		PROCESS_SET_QUOTA = 0x00000100,
		PROCESS_SET_INFORMATION = 0x00000200,
		PROCESS_QUERY_INFORMATION = 0x00000400,
		STANDARD_RIGHTS_REQUIRED = 0x000F0000,
		SYNCHRONIZE = 0x00100000,
		PROCESS_QUERY_LIMITED_INFORMATION = 0x00001000,

		PROCESS_ALL_ACCESS = PROCESS_TERMINATE | PROCESS_CREATE_THREAD | PROCESS_SET_SESSIONID | PROCESS_VM_OPERATION |
							 PROCESS_VM_READ | PROCESS_VM_WRITE | PROCESS_DUP_HANDLE | PROCESS_CREATE_PROCESS | PROCESS_SET_QUOTA |
							 PROCESS_SET_INFORMATION | PROCESS_QUERY_INFORMATION | STANDARD_RIGHTS_REQUIRED | SYNCHRONIZE
	}

	[Flags]
	internal enum SendMessageTimeoutFlags : uint
	{
		SMTO_NORMAL = 0x0,
		SMTO_BLOCK = 0x1,
		SMTO_ABORTIFHUNG = 0x2,
		SMTO_NOTIMEOUTIFNOTHUNG = 0x8,
		SMTO_ERRORONEXIT = 0x20
	}

	/// <summary>
	/// Gotten from https://stackoverflow.com/questions/12777395/using-column-imagekey-in-a-listview-to-simulate-windows-explorer-style-sorting
	/// </summary>
	[StructLayout(LayoutKind.Sequential)]
	internal struct LV_COLUMN
	{
		internal uint mask;
		internal int fmt;
		internal int cx;
		internal string pszText;
		internal int cchTextMax;
		internal int iSubItem;
		internal int iImage;
		internal int iOrder;
	}

	[StructLayout(LayoutKind.Sequential)]
	internal struct FILETIME
	{
		internal uint DateTimeLow;
		internal uint DateTimeHigh;
	}

	[Serializable, StructLayout(LayoutKind.Sequential)]
	internal struct GUITHREADINFO
	{
		internal static GUITHREADINFO Default
		{
			get
			{
				var info = new GUITHREADINFO();
				info.cbSize = (uint)Marshal.SizeOf(info);
				return info;
			}
		}

		internal uint cbSize;
		internal uint flags;
		internal nint hwndActive;
		internal nint hwndFocus;
		internal nint hwndCapture;
		internal nint hwndMenuOwner;
		internal nint hwndMoveSize;
		internal nint hwndCaret;
		internal RECT rcCaret;
	}

	[StructLayout(LayoutKind.Sequential)]
	internal struct HARDWAREINPUT
	{
		internal uint uMsg;
		internal ushort wParamL;
		internal ushort wParamH;
	}

	[StructLayout(LayoutKind.Sequential)]
	internal struct INPUT
	{
		internal uint type;
		internal INPUTDATA i;
	}

	[StructLayout(LayoutKind.Explicit)]
	internal struct INPUTDATA
	{
		[FieldOffset(0)]
		internal MOUSEINPUT m;

		[FieldOffset(0)]
		internal KEYBDINPUT k;

		[FieldOffset(0)]
		internal HARDWAREINPUT h;
	}

	[StructLayout(LayoutKind.Sequential)]
	internal struct KEYBDINPUT
	{
		internal ushort wVk;
		internal ushort wScan;
		internal uint dwFlags;
		internal uint time;
		internal ulong dwExtraInfo;
	}

	[StructLayout(LayoutKind.Sequential)]
	internal struct MOUSEINPUT
	{
		internal int dx;
		internal int dy;
		internal uint mouseData;
		internal uint dwFlags;
		internal uint time;
		internal ulong dwExtraInfo;
	}

	[StructLayout(LayoutKind.Sequential)]
	internal struct KBDLLHOOKSTRUCT
	{
		internal uint vkCode;
		internal uint scanCode;
		internal uint flags;
		internal uint time;
		internal ulong dwExtraInfo;
	}

	[StructLayout(LayoutKind.Sequential)]
	internal struct MSDLLHOOKSTRUCT
	{
		internal POINT pt;
		internal int mouseData;//Pinvoke.net claims this must be int and not uint.
		internal uint flags;
		internal int time;
		internal ulong dwExtraInfo;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct RECT//Only public for testing purposes.
	{
		public int Left;
		public int Top;
		public int Right;
		public int Bottom;
	}

	[Serializable, StructLayout(LayoutKind.Sequential)]
	internal struct WINDOWPLACEMENT
	{
		internal uint length;
		internal uint flags;
		internal uint showCmd;
		internal POINT ptMinPosition;
		internal POINT ptMaxPosition;
		internal RECT rcNormalPosition;
		internal RECT rcDevice;

		internal static WINDOWPLACEMENT Default
		{
			get
			{
				var result = new WINDOWPLACEMENT();
				result.length = (uint)Marshal.SizeOf(result);
				return result;
			}
		}
	}

	[StructLayout(LayoutKind.Sequential)]
	internal struct Msg
	{
		internal nint hwnd;
		internal uint message;
		internal nint wParam;
		internal nint lParam;
		internal uint time;
		internal POINT pt;//Apparently we can also use System.Drawing.Point, change if needed.
#if _MAC
		internal DWORD lPrivate;
#endif
	}

	[StructLayout(LayoutKind.Sequential)]
	internal struct SIZE
	{
		internal int cx;
		internal int cy;

		internal SIZE(int width, int height)
		{
			cx = width;
			cy = height;
		}
	}

	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	internal struct BLENDFUNCTION
	{
		internal byte BlendOp;
		internal byte BlendFlags;
		internal byte SourceConstantAlpha;
		internal byte AlphaFormat;
	}

	[StructLayout(LayoutKind.Sequential)]
	internal struct LASTINPUTINFO
	{
		internal static LASTINPUTINFO Default
		{
			get
			{
				var result = new LASTINPUTINFO();
				result.cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO));
				return result;
			}
		}

		[MarshalAs(UnmanagedType.U4)]
		public uint cbSize;

		[MarshalAs(UnmanagedType.U4)]
		public uint dwTime;
	}

	internal struct EventMsg
	{
		internal uint message;
		internal uint paramL;
		internal uint paramH;
		internal uint time;
		internal nint hwnd;
	}

	internal enum gaFlags : uint
	{
		GA_PARENT = 1,
		GA_ROOT = 2,
		GA_ROOTOWNER = 3
	}

	internal enum OBJID : uint
	{
		WINDOW = 0x00000000,
		SYSMENU = 0xFFFFFFFF,
		TITLEBAR = 0xFFFFFFFE,
		MENU = 0xFFFFFFFD,
		CLIENT = 0xFFFFFFFC,
		VSCROLL = 0xFFFFFFFB,
		HSCROLL = 0xFFFFFFFA,
		SIZEGRIP = 0xFFFFFFF9,
		CARET = 0xFFFFFFF8,
		CURSOR = 0xFFFFFFF7,
		ALERT = 0xFFFFFFF6,
		SOUND = 0xFFFFFFF5,
	}

	public static partial class WindowsAPI
	{
		internal static Point ToPoint(this RECT rect) => new (rect.Left, rect.Top);

		internal static Map ToPos(this RECT rect, double scale = 1.0) => new (new Dictionary<object, object>()
		{
			{ "X", rect.Left * scale },
			{ "Y", rect.Top * scale },
			{ "Width", (rect.Right - rect.Left)* scale },
			{ "Height", (rect.Bottom - rect.Top)* scale },
		});

		[DllImport(oleacc, CharSet = CharSet.Unicode)]
		internal static extern int AccessibleObjectFromWindow(nint hwnd, uint id, ref Guid iid, [In, Out, MarshalAs(UnmanagedType.IUnknown)] ref object ppvObject);

		[LibraryImport(oleaut, EntryPoint = "SysFreeString")]
		internal static partial void SysFreeString(nint bstr);


		//[DllImport(oleaut)]
		//public static extern int SafeArrayGetDim([MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_I4)] System.Array arr);
		//public static extern int SafeArrayGetDim([MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_I4)] nint arr);

		/// <summary>
		/// Gotten from https://stackoverflow.com/questions/29392625/check-if-a-winform-checkbox-is-checked-through-winapi-only
		/// </summary>
		internal static bool IsChecked(nint handle)
		{
			var guid = new Guid("{618736E0-3C3D-11CF-810C-00AA00389B71}");
			object obj = null;
			var retValue = AccessibleObjectFromWindow(handle, (uint)OBJID.CLIENT, ref guid, ref obj);

			if (obj is IAccessible accObj)
			{
				var result = accObj.get_accState(0);

				if (result is int state)
					return state == CHECKED || state == CHECKED_FOCUSED;
			}

			return false;
		}

		[LibraryImport(user32, EntryPoint = "PeekMessageW", StringMarshalling = StringMarshalling.Utf16)]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool PeekMessage(out Msg message, nint handle, uint filterMin, uint filterMax, uint flags);

		[LibraryImport(user32, EntryPoint = "MonitorFromRect")]
		internal static partial nint MonitorFromRect(ref RECT rect, uint flags);

		[LibraryImport("shcore.dll", EntryPoint = "GetDpiForMonitor")]
		internal static partial int GetDpiForMonitor(nint monitor, int dpiType, out uint dpiX, out uint dpiY);

		[LibraryImport(user32, EntryPoint = "OpenClipboard")]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool OpenClipboard(nint hWndNewOwner);

		[LibraryImport(user32, EntryPoint = "CloseClipboard")]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool CloseClipboard();

		[LibraryImport(user32, EntryPoint = "EmptyClipboard")]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool EmptyClipboard();

		[LibraryImport(user32, EntryPoint = "IsClipboardFormatAvailable")]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool IsClipboardFormatAvailable(uint format);

		internal static bool OpenClipboard(long ms)
		{
			bool open;
			var dtStart = DateTime.UtcNow;

			while (!(open = OpenClipboard(0)))
			{
				if (ms == -1)
					break;

				if (ms == 0)
					break;

				if ((DateTime.UtcNow - dtStart).TotalMilliseconds > ms)
					break;

				// Pump messages (so the clipboard's current owner can respond) but stay uninterruptible while waiting:
				// a hotkey or timer launched here might itself touch the clipboard and corrupt this operation. Matches
				// AutoHotkey's SLEEP_WITHOUT_INTERRUPTION in Clipboard::Open().
				Keysharp.Internals.Flow.SleepWithoutInterruption(100);
			}

			return open;
		}

		internal static nint GetClipboardData(int format, ref bool nullIsOkay)
		{
			nullIsOkay = false;
			var formatName = "";

			if (format < 0xC000 || format > 0xFFFF) // It's a registered format (you're supposed to verify in-range before calling GetClipboardFormatName()).  Also helps performance.
			{
			}
			else
			{
				var fmt = DataFormats.GetFormat(format);

				if (fmt != null)
					formatName = fmt.Name;

				if (formatName.StartsWith("Link Source", StringComparison.OrdinalIgnoreCase)
						|| formatName.StartsWith("ObjectLink", StringComparison.OrdinalIgnoreCase)
						|| formatName.StartsWith("OwnerLink", StringComparison.OrdinalIgnoreCase)
						|| formatName.StartsWith("Native", StringComparison.OrdinalIgnoreCase)
						|| formatName.StartsWith("Embed Source", StringComparison.OrdinalIgnoreCase))
					return 0;

				if (formatName.StartsWith("MSDEVColumnSelect", StringComparison.OrdinalIgnoreCase)
						|| formatName.StartsWith("MSDEVLineSelect", StringComparison.OrdinalIgnoreCase))
				{
					nullIsOkay = true;
					return 0;
				}
			}

			return GetClipboardData((uint)format);
		}

		[LibraryImport(kernel32, EntryPoint = "GlobalSize")]
		internal static partial nint GlobalSize(nint handle);

		[LibraryImport(kernel32, EntryPoint = "GlobalLock")]
		internal static partial nint GlobalLock(nint hMem);

		[LibraryImport(kernel32, EntryPoint = "GlobalUnlock")]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool GlobalUnlock(nint hMem);

		[LibraryImport(user32, EntryPoint = "GetClipboardData")]
		internal static partial nint GetClipboardData(uint uFormat);

		[LibraryImport(user32, EntryPoint = "SetClipboardData")]
		internal static partial nint SetClipboardData(uint uFormat, nint hMem);

		/// <summary>Walks the clipboard's advertised formats (0 starts the walk, 0 ends it). The clipboard must be
		/// open. This is the exact answer to "what is on the clipboard", including registered/private formats — the
		/// reason it replaced a probe over a fixed list of well-known format names.</summary>
		[LibraryImport(user32, EntryPoint = "EnumClipboardFormats", SetLastError = true)]
		internal static partial uint EnumClipboardFormats(uint format);

		[LibraryImport(kernel32, EntryPoint = "GlobalAlloc")]
		internal static partial nint GlobalAlloc(uint uFlags, nint dwBytes);

		[LibraryImport(kernel32, EntryPoint = "GlobalFree")]
		internal static partial nint GlobalFree(nint hMem);

		/// <summary>A GMEM_MOVEABLE copy of <paramref name="bytes"/>, ready to hand to
		/// <see cref="SetClipboardData"/> (which takes ownership on success). Returns 0 on failure.</summary>
		internal static nint GlobalCopy(ReadOnlySpan<byte> bytes)
		{
			var handle = GlobalAlloc(GMEM_MOVEABLE, bytes.Length);

			if (handle == 0)
				return 0;

			var ptr = GlobalLock(handle);

			if (ptr == 0)
			{
				_ = GlobalFree(handle);
				return 0;
			}

			unsafe
			{
				bytes.CopyTo(new Span<byte>((void*)ptr, bytes.Length));
			}

			_ = GlobalUnlock(handle);
			return handle;
		}

		[LibraryImport(dwmapi, EntryPoint = "DwmGetWindowAttribute")]
		internal static partial uint DwmGetWindowAttribute(nint hwnd, DWMWINDOWATTRIBUTE dwAttribute, ref int pvAttribute, int cbsize);

		internal static bool IsWindowCloaked(nint hwnd)
		{
			var cloaked = 0;
			return DwmGetWindowAttribute(hwnd, DWMWINDOWATTRIBUTE.DWMWA_CLOAKED, ref cloaked, 4) >= 0 && cloaked != 0;
		}

		[LibraryImport(user32, EntryPoint = "GetLastActivePopup")]
		internal static partial nint GetLastActivePopup(nint hWnd);

		[LibraryImport(user32, EntryPoint = "GetShellWindow")]
		internal static partial nint GetShellWindow();

		[LibraryImport(user32, EntryPoint = "BlockInput")]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool BlockInput([MarshalAs(UnmanagedType.Bool)] bool fBlockIt);

		[LibraryImport(user32, EntryPoint = "CallNextHookEx")]
		internal static partial nint CallNextHookEx(nint hhk, int nCode, nint wParam, ref EventMsg lParam);

		[LibraryImport(user32, EntryPoint = "CallNextHookEx")]
		internal static partial nint CallNextHookEx(nint hhk, int nCode, nint wParam, ref KBDLLHOOKSTRUCT lParam);

		[LibraryImport(user32, EntryPoint = "CallNextHookEx")]
		internal static partial nint CallNextHookEx(nint hhk, int nCode, nint wParam, ref MSDLLHOOKSTRUCT lParam);

		[LibraryImport(user32, EntryPoint = "CallNextHookEx")]
		internal static partial nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

		[LibraryImport(kernel32, EntryPoint = "CloseHandle")]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool CloseHandle(nint hObject);

		[LibraryImport(user32, EntryPoint = "CloseWindow")]
		internal static partial int CloseWindow(nint hWnd);

		[LibraryImport(kernel32, EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
		internal static partial nint CreateFile(string fileName,
											   uint desiredAccess,
											   uint shareMode,
											   nint attributes,
											   uint creationDisposition,
											   uint flagsAndAttributes,
											   nint templateFile);

		[LibraryImport(user32, EntryPoint = "DestroyWindow")]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool DestroyWindow(nint hwnd);

		[DllImport(kernel32, CharSet = CharSet.Unicode, SetLastError = true)]
		internal static extern bool DeviceIoControl(
			nint hDevice,
			uint dwIoControlCode,
			ref long InBuffer,
			int nInBufferSize,
			ref long OutBuffer,
			int nOutBufferSize,
			ref int pBytesReturned,
			[In] ref NativeOverlapped lpOverlapped);

		[LibraryImport(user32, EntryPoint = "DestroyIcon")]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool DestroyIcon(nint handle);

		//[DllImport(kernel32, SetLastError = true)]
		//internal static extern bool DeviceIoControl(nint driveHandle,
		//      uint IoControlCode,
		//      nint lpInBuffer,
		//      uint inBufferSize,
		//      nint lpOutBuffer,
		//      uint outBufferSize,
		//      out uint lpBytesReturned,
		//      nint lpOverlapped);
		[DllImport(winmm, CharSet = CharSet.Unicode)]
		internal static extern int mciSendString(string command, StringBuilder buffer, int bufferSize, nint hwndCallback);

		//SoundPlay's "*n": the parameter is the MB_ICON* value, and 0xFFFFFFFF (-1) is the simple beep.
		[LibraryImport(user32, EntryPoint = "MessageBeep")]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool MessageBeep(uint uType);

		[LibraryImport(winmm, EntryPoint = "joyGetPosEx")]
		internal static partial int joyGetPosEx(int uJoyID, ref JOYINFOEX pji);

		[DllImport(winmm, CharSet = CharSet.Unicode)]
		internal static extern int joyGetDevCaps(nint id, ref JOYCAPS lpCaps, uint uSize);

		[LibraryImport(user32, EntryPoint = "SetProcessDPIAware")]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool SetProcessDPIAware();

		[LibraryImport(user32, EntryPoint = "IsHungAppWindow")]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool IsHungAppWindow(nint hWnd);

		[LibraryImport(user32, EntryPoint = "EnableWindow")]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool EnableWindow(nint hWnd, [MarshalAs(UnmanagedType.Bool)] bool bEnable);

		[LibraryImport(user32, EntryPoint = "EnumChildWindows")]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool EnumChildWindows(nint hwndParent, _EnumWindowsProc lpEnumFunc, nint lParam);

		[LibraryImport(user32, EntryPoint = "EnumWindows")]
		internal static partial int EnumWindows(_EnumWindowsProc lpEnumFunc, nint lParam);

		[LibraryImport(user32, EntryPoint = "ExitWindowsEx")]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool ExitWindowsEx(uint uFlags, uint dwReason);

		[LibraryImport(shell32, EntryPoint = "ExtractIconW", StringMarshalling = StringMarshalling.Utf16)]
		internal static partial nint ExtractIcon(nint hInst, string lpszExeFileName, int nIconIndex);

		[LibraryImport(user32, EntryPoint = "FindWindowW", StringMarshalling = StringMarshalling.Utf16)]

		internal static partial nint FindWindow(string className, string windowName);

		[LibraryImport(user32, EntryPoint = "FindWindowExW", StringMarshalling = StringMarshalling.Utf16)]
		internal static partial nint FindWindowEx(nint parentHandle, nint childAfter, string className, string windowTitle);

		[LibraryImport(user32, EntryPoint = "FlashWindow")]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool FlashWindow(nint hWnd, [MarshalAs(UnmanagedType.Bool)] bool bInvert);

		[LibraryImport(kernel32, EntryPoint = "FreeLibrary")]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool FreeLibrary(nint hModule);

		[LibraryImport(kernel32, EntryPoint = "GetCurrentThreadId")]
		internal static partial uint GetCurrentThreadId();

		[LibraryImport(user32, EntryPoint = "AttachThreadInput")]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool fAttach);

		internal static (bool, uint) AttachThreadInput(nint targetWindow, bool setActive)
		{
			var threadsAreAttached = false;
			var targetThread = GetWindowThreadProcessId(targetWindow, out _);
			var tid = Platform.Process.CurrentThreadId();

			if (targetThread != 0 && targetThread != tid && !IsHungAppWindow(targetWindow))
				threadsAreAttached = AttachThreadInput(tid, targetThread, true);

			if (setActive)
				_ = SetActiveWindow(targetWindow);

			return (threadsAreAttached, targetThread);
		}

		internal static void DetachThreadInput(bool threadsAreAttached, uint targetThread)
		{
			if (threadsAreAttached)
				_ = AttachThreadInput(Platform.Process.CurrentThreadId(), targetThread, false);
		}

		internal static nint AllocInterProcMem(uint size, nint hwnd, ProcessAccessTypes extraAccess, out nint handle)
		// aHandle is an output parameter that receives the process handle.
		// Returns NULL on failure (in which case caller should ignore the value of aHandle).
		{
			nint mem = 0;
			_ = GetWindowThreadProcessId(hwnd, out var pid);

			// Even if the PID is our own, open the process anyway to simplify the code. After all, it would be
			// pretty silly for a script to access its own ListViews via this method.
			if ((handle = OpenProcess(ProcessAccessTypes.PROCESS_VM_OPERATION | ProcessAccessTypes.PROCESS_VM_READ | ProcessAccessTypes.PROCESS_VM_WRITE | extraAccess, false, pid)) == 0)
				return mem;

			// Reason for using VirtualAllocEx(): When sending LVITEM structures to a control in a remote process, the
			// structure and its pszText buffer must both be memory inside the remote process rather than in our own.
			mem = VirtualAllocEx(handle, 0, size, VirtualAllocExTypes.MEM_RESERVE | VirtualAllocExTypes.MEM_COMMIT, AccessProtectionFlags.PAGE_READWRITE);

			//
			if (mem == 0)
				_ = CloseHandle(handle); // Caller should ignore the value of aHandle when return value is NULL.

			//else leave the handle open.  It's the caller's responsibility to close it.
			return mem;
		}

		[LibraryImport(user32, EntryPoint = "GetActiveWindow")]
		internal static partial nint GetActiveWindow(nint hWnd);

		[LibraryImport(user32, EntryPoint = "GetActiveWindow")]
		internal static partial nint GetActiveWindow();

		[LibraryImport(user32, EntryPoint = "GetAncestor")]
		internal static partial nint GetAncestor(nint hWnd, gaFlags gaFlags);

		[LibraryImport(user32, EntryPoint = "GetClassNameW", StringMarshalling = StringMarshalling.Utf16)]
		internal static unsafe partial int GetClassName(nint hWnd, [Out] char[] lpClassName, int nMaxCount);

		internal static string GetClassName(nint hwnd)
		{
			var buffer = new char[256];
			int len = GetClassName(hwnd, buffer, buffer.Length);
			return new string(buffer, 0, len);
		}

		[LibraryImport(user32, EntryPoint = "GetCursorPos")]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool GetCursorPos(out POINT lpPoint);

		[LibraryImport(user32, EntryPoint = "SetCursorPos")]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool SetCursorPos(int x, int y);

		[LibraryImport(user32, EntryPoint = "GetDesktopWindow")]
		internal static partial long GetDesktopWindow();

		[LibraryImport(version, EntryPoint = "GetFileVersionInfoW", StringMarshalling = StringMarshalling.Utf16)]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool GetFileVersionInfo(string sFileName, int handle, int size, byte[] infoBuffer);

		[LibraryImport(version, EntryPoint = "GetFileVersionInfoSizeW", StringMarshalling = StringMarshalling.Utf16)]
		internal static partial int GetFileVersionInfoSize(string sFileName, out int handle);

		[LibraryImport(user32, EntryPoint = "GetFocus")]
		internal static partial nint GetFocus();

		[LibraryImport(user32, EntryPoint = "GetDC")]
		internal static partial nint GetDC(nint hwnd);

		// GetWindowDC returns a DC for the entire window (title bar and borders included), unlike GetDC
		// which is the client area only.
		[LibraryImport(user32, EntryPoint = "GetWindowDC")]
		internal static partial nint GetWindowDC(nint hwnd);

		[LibraryImport(user32, EntryPoint = "GetParent")]
		internal static partial nint GetParent(nint hWnd);

		[LibraryImport(user32, EntryPoint = "GetForegroundWindow")]
		internal static partial nint GetForegroundWindow();

		// lpgui must be passed by ref (not out): GetGUIThreadInfo requires the caller to set cbSize
		// before the call, and an `out` marshaller would zero-initialize the struct (cbSize=0 -> the API
		// returns FALSE every time). Callers pass a GUITHREADINFO.Default whose cbSize is populated.
		[LibraryImport(user32, EntryPoint = "GetGUIThreadInfo")]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);

		[LibraryImport(user32, EntryPoint = "ActivateKeyboardLayout")]
		internal static partial int ActivateKeyboardLayout(nint hkl, uint Flags);

		[LibraryImport(user32, EntryPoint = "GetKeyboardLayout")]
		internal static partial nint GetKeyboardLayout(uint idThread);

		[LibraryImport(user32, EntryPoint = "LoadKeyboardLayoutW", StringMarshalling = StringMarshalling.Utf16)]
		internal static partial nint LoadKeyboardLayout(string pwszKLID, uint Flags);

		[LibraryImport(user32, EntryPoint = "GetKeyboardLayoutNameW", StringMarshalling = StringMarshalling.Utf16)]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool GetKeyboardLayoutName([Out] char[] pwszKLID);

		[LibraryImport(user32, EntryPoint = "GetKeyboardState")]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool GetKeyboardState(byte[] lpKeyState);

		[LibraryImport(user32, EntryPoint = "SetKeyboardState")]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool SetKeyboardState(byte[] lpKeyState);

		[LibraryImport(user32, EntryPoint = "GetKeyState")]
		internal static partial short GetKeyState(int nVirtKey);

		[LibraryImport(user32, EntryPoint = "GetAsyncKeyState")]
		internal static partial short GetAsyncKeyState(int nVirtKey);

		[LibraryImport(user32, EntryPoint = "keybd_event")]
		internal static partial void keybd_event(byte bVk, byte bScan, uint dwFlags, uint dwExtraInfo);

		[LibraryImport(user32, EntryPoint = "GetMenu")]
		internal static partial nint GetMenu(nint hWnd);

		[LibraryImport(user32, EntryPoint = "GetSystemMenu")]
		internal static partial nint GetSystemMenu(nint hWnd, [MarshalAs(UnmanagedType.Bool)] bool revert);

		[LibraryImport(user32, EntryPoint = "GetMenuItemCount")]
		internal static partial int GetMenuItemCount(nint hMenu);

		[DllImport(user32, CharSet = CharSet.Unicode)]
		internal static extern int GetMenuString(nint hMenu, uint uIDItem, [Out] StringBuilder lpString, int nMaxCount, uint uFlag);

		[LibraryImport(user32, EntryPoint = "IsDialogMessageW")]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool IsDialogMessage(nint hDlg, Msg lpMsg);

		[LibraryImport(user32, EntryPoint = "GetMessageW")]
		internal static partial int GetMessage(out Msg lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

		[LibraryImport(user32, EntryPoint = "TranslateMessage")]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool TranslateMessage(in Msg lpMsg);

		[LibraryImport(user32, EntryPoint = "DispatchMessageW")]
		internal static partial nint DispatchMessage(in Msg lpMsg);

		[LibraryImport(user32, EntryPoint = "GetMessageExtraInfo")]
		internal static partial nint GetMessageExtraInfo();

		[LibraryImport(kernel32, EntryPoint = "GetModuleHandleExW", StringMarshalling = StringMarshalling.Utf16)]
		internal static partial int GetModuleHandleEx(uint dwFlags, string lpModuleName, out nint phModule);

		[LibraryImport(kernel32, EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
		internal static partial nint GetModuleHandle(string lpModuleName);

		[LibraryImport(kernel32, EntryPoint = "GetPrivateProfileStringW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
		internal static partial uint GetPrivateProfileString(string lpAppName, string lpKeyName, string lpDefault, [Out] char[] lpReturnedString, uint nSize, string lpFileName);

		[LibraryImport(kernel32, EntryPoint = "GetPrivateProfileSectionW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
		internal static partial uint GetPrivateProfileSection(string lpAppName, [Out] char[] lpszReturnBuffer, uint nSize, string lpFileName);

		[LibraryImport(kernel32, EntryPoint = "GetPrivateProfileSectionNamesW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
		internal static partial uint GetPrivateProfileSectionNames([Out] char[] lpszReturnBuffer, uint nSize, string lpFileName);

		/// <summary>
		/// Set this to use ExactSpelling since GetProcAddress() only is available in ANSI.
		/// </summary>
		/// <param name="hModule"></param>
		/// <param name="procName"></param>
		/// <returns></returns>
		[LibraryImport(kernel32, EntryPoint = "GetProcAddress", SetLastError = true)]
		internal static partial nint GetProcAddress(nint hModule, [MarshalAs(UnmanagedType.LPStr)] string procName);

		[LibraryImport(user32, EntryPoint = "GetSubMenu")]
		internal static partial nint GetSubMenu(nint hMenu, int nPos);

		[LibraryImport(user32, EntryPoint = "GetMenuItemID")]
		internal static partial uint GetMenuItemID(nint hMenu, int nPos);

		[LibraryImport(kernel32, EntryPoint = "SetLastError")]
		internal static partial void SetLastError(uint dwErrCode);

		[LibraryImport(kernel32, EntryPoint = "GetLastError")]
		internal static partial uint GetLastError();

		[LibraryImport(user32, EntryPoint = "GetDlgCtrlID")]
		internal static partial int GetDlgCtrlID(nint hwndCtl);

		[LibraryImport(user32, EntryPoint = "GetWindow")]
		internal static partial nint GetWindow(nint hWnd, uint uCmd);

		[LibraryImport(user32, EntryPoint = "GetLastInputInfo")]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool GetLastInputInfo(ref LASTINPUTINFO plii);

		[LibraryImport(user32, EntryPoint = "GetLayeredWindowAttributes")]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool GetLayeredWindowAttributes(nint hwnd, out uint crKey, out byte bAlpha, out uint dwFlags);

		[LibraryImport(user32, EntryPoint = "GetWindowPlacement")]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool GetWindowPlacement(nint hWnd, out WINDOWPLACEMENT lpwndpl);

		[LibraryImport(user32, EntryPoint = "GetWindowRect")]
		[return: MarshalAs(UnmanagedType.Bool)]

		internal static partial bool GetWindowRect(nint hWnd, out RECT lpRect);

		[LibraryImport(user32, EntryPoint = "GetClientRect")]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool GetClientRect(nint hWnd, out RECT lpRect);

		// PW_CLIENTONLY (0x1) renders only the client area (no title bar / borders); PW_RENDERFULLCONTENT
		// (0x2) renders DirectComposition/hardware-accelerated content too, so the capture works for
		// modern (UWP, Chromium, etc.) windows, not just GDI ones.
		internal const uint PW_CLIENTONLY = 0x00000001;
		internal const uint PW_RENDERFULLCONTENT = 0x00000002;

		// SRCCOPY raster-op for BitBlt: copy the source rectangle directly to the destination.
		internal const uint SRCCOPY = 0x00CC0020;

		[LibraryImport(user32, EntryPoint = "PrintWindow")]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool PrintWindow(nint hWnd, nint hdcBlt, uint nFlags);

		[LibraryImport(gdi32, EntryPoint = "BitBlt")]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool BitBlt(nint hdc, int x, int y, int cx, int cy, nint hdcSrc, int x1, int y1, uint rop);

		[LibraryImport(user32, EntryPoint = "FillRect")]
		internal static partial int FillRect(nint hDC, ref RECT lprc, nint hbr);


		[LibraryImport(gdi32, EntryPoint = "GetTextColor")]
		internal static partial uint GetTextColor(nint hdc);

		[LibraryImport(gdi32, EntryPoint = "GetBkColor")]
		internal static partial uint GetBkColor(nint hdc);


		[LibraryImport(user32, EntryPoint = "RedrawWindow")]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool RedrawWindow(nint hWnd, nint lprcUpdate, nint hrgnUpdate, uint flags);

		[LibraryImport(user32, EntryPoint = "MapWindowPoints")]
		internal static partial int MapWindowPoints(nint hWndFrom, nint hWndTo, ref RECT rect, uint cPoints);

		[LibraryImport(user32, EntryPoint = "ClientToScreen")]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool ClientToScreen(nint hWnd, ref POINT lpPoint);

		[LibraryImport(user32, EntryPoint = "ScreenToClient")]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool ScreenToClient(nint hWnd, ref POINT lpPoint);

		[LibraryImport(user32, EntryPoint = "ReleaseDC")]
		internal static partial int ReleaseDC(nint hWnd, nint hDC);

		[LibraryImport(user32, EntryPoint = "GetWindowTextW", StringMarshalling = StringMarshalling.Utf16)]
		internal static partial int GetWindowText(nint hWnd, [Out] char[] lpString, int nMaxCount);

		//void CoordToScreen(POINT &aPoint, int aWhichMode)
		//// For convenience. See function above for comments.
		//{
		//  CoordToScreen((int &)aPoint.x, (int &)aPoint.y, aWhichMode);
		//}

		[LibraryImport(user32, EntryPoint = "mouse_event")]
		internal static partial void mouse_event(uint dwFlags, int dx, int dy, uint dwData, nint dwExtraInfo);

		/// <summary>
		/// See http://msdn.microsoft.com/en-us/library/ms632599%28VS.85%29.aspx#message_only
		/// </summary>
		/// <param name="hwnd"></param>
		/// <returns></returns>
		[LibraryImport(user32, EntryPoint = "AddClipboardFormatListener")]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool AddClipboardFormatListener(nint hwnd);

		[LibraryImport(user32, EntryPoint = "RemoveClipboardFormatListener")]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool RemoveClipboardFormatListener(nint hwnd);

		internal static string GetWindowText(nint hwnd)
		{
			int textLength = WindowsAPI.GetWindowTextLength(hwnd);

			if (textLength == 0)
				return string.Empty;

			char[] outText = new char[textLength + 1];
			int a = GetWindowText(hwnd, outText, outText.Length);
			return new string(outText, 0, a);
		}

		[LibraryImport(user32, EntryPoint = "GetWindowTextLengthW")]
		internal static partial int GetWindowTextLength(nint hWnd);

		[LibraryImport(user32, EntryPoint = "GetWindowThreadProcessId"), SuppressGCTransition]
		internal static partial uint GetWindowThreadProcessId(nint hWnd, out uint processId);

		[LibraryImport(user32, EntryPoint = "InvalidateRect")]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool InvalidateRect(nint hWnd, nint lpRect, [MarshalAs(UnmanagedType.Bool)] bool bErase);

		[LibraryImport(user32, EntryPoint = "IsWindow")]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool IsWindow(nint hWnd);

		[LibraryImport(user32, EntryPoint = "IsWindowEnabled")]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool IsWindowEnabled(nint hWnd);

		[LibraryImport(user32, EntryPoint = "IsWindowVisible")]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool IsWindowVisible(nint hWnd);

		[LibraryImport(user32, EntryPoint = "IsChild")]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool IsChild(nint hWndParent, nint hWnd);

		[LibraryImport(kernel32, EntryPoint = "SetDllDirectoryW", StringMarshalling = StringMarshalling.Utf16)]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool SetDllDirectory(string lpPathName);

		[LibraryImport(kernel32, EntryPoint = "LoadLibraryW", StringMarshalling = StringMarshalling.Utf16)]
		internal static partial nint LoadLibrary(string lpFileName);

		[LibraryImport(user32, EntryPoint = "VkKeyScanExW")]
		internal static partial short VkKeyScanEx([MarshalAs(UnmanagedType.U2)] char ch, nint dwhkl);

		[LibraryImport(user32, EntryPoint = "MapVirtualKeyW")]
		internal static partial uint MapVirtualKey(uint uCode, uint uMapType);

		[LibraryImport(user32, EntryPoint = "MapVirtualKeyExW")]
		internal static partial uint MapVirtualKeyEx(uint uCode, uint uMapType, nint dwhkl);

		[LibraryImport(user32, EntryPoint = "MoveWindow")]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool MoveWindow(nint hWnd, int X, int Y, int nWidth, int nHeight, [MarshalAs(UnmanagedType.Bool)] bool bRepaint);

		//[DllImport(kernel32, CharSet = CharSet.Unicode)]
		//internal static extern nint OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

		[LibraryImport(kernel32, EntryPoint = "OutputDebugStringW", StringMarshalling = StringMarshalling.Utf16)]
		internal static partial void OutputDebugString(string lpOutputString);

		[LibraryImport(user32, EntryPoint = "PostQuitMessage")]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial void PostQuitMessage(int nExitCode);

		[LibraryImport(user32, EntryPoint = "PostThreadMessageW")]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool PostThreadMessage(uint threadId, uint msg, nint wParam, nint lParam);

		[LibraryImport(user32, EntryPoint = "PostMessageW", StringMarshalling = StringMarshalling.Utf16)]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool PostMessage(nint hWnd, uint msg, nint wParam, nint lParam);

		[LibraryImport(user32, EntryPoint = "PostMessageW")]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool PostMessage(nint hWnd, uint msg, uint wParam, uint lParam);

		[LibraryImport(user32, EntryPoint = "PostMessageW", StringMarshalling = StringMarshalling.Utf16)]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool PostMessage(nint hWnd, uint msg, string wParam, nint lParam);

		[LibraryImport(user32, EntryPoint = "GetMessageTime")]
		internal static partial long GetMessageTime();

		[LibraryImport(user32, EntryPoint = "RealChildWindowFromPoint")]
		internal static partial nint RealChildWindowFromPoint(nint hwndParent, POINT ptParentClientCoords);

		[LibraryImport(user32, EntryPoint = "SendInput")]
		internal static partial uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

		[LibraryImport(user32, EntryPoint = "SendMessageW")]
		internal static partial uint SendMessage(nint hWnd, uint msg, uint wParam, uint lParam);

		[LibraryImport(user32, EntryPoint = "SendMessageW")]
		internal static partial nint SendMessage(nint hWnd, uint msg, nint wParam, nint lParam);

		[LibraryImport(user32, EntryPoint = "SendMessageW")]
		internal static partial nint SendMessage(nint hWnd, uint msg, int wParam, int[] lParam);

		[LibraryImport(user32, EntryPoint = "SendMessageW")]
		internal static partial nint SendMessage(nint hWnd, uint msg, int wParam, [MarshalAs(UnmanagedType.LPWStr)] string lParam);

		[DllImport(user32, CharSet = CharSet.Unicode)]
		internal static extern nint SendLVColMessage(nint hWnd, uint msg, uint wParam, ref LV_COLUMN lParam);

		[LibraryImport(user32, EntryPoint = "IsIconic")]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool IsIconic(nint Hwnd);

		[LibraryImport(user32, EntryPoint = "IsZoomed")]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool IsZoomed(nint hWnd);

		[LibraryImport(shell32, EntryPoint = "SHGetKnownFolderPath", SetLastError = true)]
		internal static partial int SHGetKnownFolderPath(
			Guid rfid,
			uint dwFlags,
			nint hToken,
			out nint pszPath);

		/// <summary>
		/// Gotten from https://www.codeproject.com/Questions/1224984/How-to-get-quick-access-folder-path-in-windows-usi
		/// </summary>
		/// <param name="rfid"></param>
		/// <returns></returns>
		internal static string SHGetKnownFolderPath(Guid guid)
		{
			nint pPath = 0;
			string path = null;

			try
			{
				var hr = SHGetKnownFolderPath(guid, (uint)KnownFolderFlag.None, 0, out pPath);

				if (hr == 0)
				{
					//throw Marshal.GetExceptionForHR(hr);
					path = Marshal.PtrToStringUni(pPath);
				}
			}
			finally
			{
				if (pPath != 0)
				{
					Marshal.FreeCoTaskMem(pPath);
					pPath = 0;
				}
			}

			return path;
		}

		/// <summary>
		/// Gotten from https://gist.github.com/BoyCook/5075907
		/// </summary>
		internal struct COPYDATASTRUCT
		{
			public nint dwData;
			public int cbData;

			[MarshalAs(UnmanagedType.LPWStr)]
			public string lpData;
		}

		//For use with WM_COPYDATA and COPYDATASTRUCT
		[DllImport(user32, CharSet = CharSet.Unicode)]
		internal static extern long SendMessageTimeout(nint hWnd, uint Msg, nint wParam, ref COPYDATASTRUCT lParam,
				SendMessageTimeoutFlags flags,
				uint timeout,
				out nint result);

		//For use with WM_COPYDATA and COPYDATASTRUCT
		[DllImport(user32, CharSet = CharSet.Unicode)]
		internal static extern int PostMessage(nint hWnd, uint Msg, nint wParam, ref COPYDATASTRUCT lParam);


		[LibraryImport(user32, EntryPoint = "SendMessageTimeoutW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
		internal static partial long SendMessageTimeout(
			nint hWnd,
			uint Msg,
			nint wParam,
			string lParam,
			SendMessageTimeoutFlags flags,
			uint timeout,
			out nint result);

		[LibraryImport(user32, EntryPoint = "SendMessageTimeoutW", SetLastError = true)]
		internal static partial long SendMessageTimeout(
			nint hWnd,
			uint Msg,
			ref uint wParam,
			ref uint lParam,
			SendMessageTimeoutFlags flags,
			uint timeout,
			out nint result);

		[LibraryImport(user32, EntryPoint = "SendMessageTimeoutW", SetLastError = true)]
		internal static partial long SendMessageTimeout(
			nint hWnd,
			uint Msg,
			ref nint wParam,
			ref nint lParam,
			SendMessageTimeoutFlags flags,
			uint timeout,
			out nint result);

		[LibraryImport(user32, EntryPoint = "SendMessageTimeoutW", SetLastError = true)]
		internal static partial long SendMessageTimeout(
			nint hWnd,
			uint Msg,
			nint wParam,
			nint lParam,
			SendMessageTimeoutFlags flags,
			uint timeout,
			out nint result);

		[LibraryImport(user32, EntryPoint = "SendMessageTimeoutW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
		internal static partial long SendMessageTimeout(
			nint hWnd,
			uint Msg,
			nint wParam,
			char[] lParam,
			SendMessageTimeoutFlags flags,
			uint timeout,
			out nint result);

		[DllImport(advapi, CharSet = CharSet.Unicode)]
		internal static extern int RegQueryInfoKey(
			SafeRegistryHandle hKey,
			[Out()] StringBuilder lpClass,
			ref uint lpcchClass,
			nint lpReserved,
			nint lpcSubKeys,
			nint lpcbMaxSubKeyLen,
			nint lpcbMaxClassLen,
			nint lpcValues,
			nint lpcbMaxValueNameLen,
			nint lpcbMaxValueLen,
			nint lpcbSecurityDescriptor,
			out long lpftLastWriteTime);

		[LibraryImport(kernel32, EntryPoint = "GetShortPathNameW", StringMarshalling = StringMarshalling.Utf16)]
		internal static partial int GetShortPathName(string longPath, [Out] char[] shortPath, int bufSize);

		[LibraryImport(kernel32, EntryPoint = "GetCurrentProcessId")]
		internal static partial uint GetCurrentProcessId();

		[LibraryImport(user32, EntryPoint = "SetActiveWindow")]
		internal static partial int SetActiveWindow(nint handle);

		[LibraryImport(user32, EntryPoint = "BringWindowToTop")]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool BringWindowToTop(nint hWnd);

		[LibraryImport(user32, EntryPoint = "SetFocus")]
		internal static partial nint SetFocus(nint hWnd);

		[LibraryImport(user32, EntryPoint = "SetForegroundWindow")]
		[return: MarshalAs(UnmanagedType.Bool)]

		internal static partial bool SetForegroundWindow(nint hWnd);

		[LibraryImport(user32, EntryPoint = "SetLayeredWindowAttributes")]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool SetLayeredWindowAttributes(nint hwnd, uint crKey, byte bAlpha, uint dwFlags);

		[DllImport(user32, SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static extern bool UpdateLayeredWindow(nint hwnd, nint hdcDst, ref POINT pptDst, ref SIZE psize, nint hdcSrc, ref POINT pptSrc, uint crKey, ref BLENDFUNCTION pblend, uint dwFlags);

		//[DllImport(user32, CharSet = CharSet.Unicode)]
		//internal static extern int SetWindowLong(nint hWnd, int nIndex, int dwNewLong);
		[LibraryImport(user32, EntryPoint = "GetWindowLongPtrW")]
		internal static partial nint GetWindowLongPtr(nint hWnd, int nIndex);

		[LibraryImport(user32, EntryPoint = "SetWindowLongPtrW")]
		internal static partial nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

		[LibraryImport(user32, EntryPoint = "SetWindowPos")]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

		[LibraryImport(user32, EntryPoint = "SetWindowsHookExW")]
		internal static partial nint SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, nint hMod, uint dwThreadId);

		[LibraryImport(user32, EntryPoint = "SetWindowsHookExW")]
		internal static partial nint SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, nint hMod, uint dwThreadId);

		[LibraryImport(user32, EntryPoint = "SetWindowsHookExW")]
		internal static partial nint SetWindowsHookEx(int idHook, PlaybackProc lpfn, nint hMod, uint dwThreadId);

		[LibraryImport(user32, EntryPoint = "SetWinEventHook")]
		internal static partial nint SetWinEventHook(uint eventMin, uint eventMax, nint hmodWinEventProc, WinEventProc pfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

		[LibraryImport(user32, EntryPoint = "UnhookWinEvent")]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool UnhookWinEvent(nint hWinEventHook);

		//[DllImport(user32, CharSet = CharSet.Unicode)]
		//internal static extern nint CallNextHookEx(nint hhk, int nCode, int wParam, [In] KBDLLHOOKSTRUCT lParam);
		//[DllImport(user32, CharSet = CharSet.Unicode)]
		//internal static extern nint CallNextHookEx(nint hhk, int nCode, int wParam, [In] MSDLLHOOKSTRUCT lParam);

		[LibraryImport(user32, EntryPoint = "SetWindowTextW", StringMarshalling = StringMarshalling.Utf16)]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool SetWindowText(nint hWnd, string lpString);

		[LibraryImport(shell32, EntryPoint = "SHEmptyRecycleBinW", StringMarshalling = StringMarshalling.Utf16)]
		internal static partial int SHEmptyRecycleBin(nint hWnd, string pszRootPath, uint dwFlags);

		[LibraryImport(user32, EntryPoint = "ShowWindow")]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool ShowWindow(nint hWnd, int nCmdShow);

		[LibraryImport(kernel32, EntryPoint = "TerminateProcess")]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool TerminateProcess(nint hProcess, uint uExitCode);

		[LibraryImport(user32, StringMarshalling = StringMarshalling.Utf16)]
		internal static partial int ToUnicodeEx(uint wVirtKey, uint wScanCode, byte[] lpKeyState, [Out] char[] pwszBuff, int cchBuff, uint wFlags, nint dwhkl);

		[LibraryImport(user32, EntryPoint = "UnhookWindowsHookEx")]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool UnhookWindowsHookEx(nint hhk);

		[LibraryImport(version, EntryPoint = "VerQueryValueW", StringMarshalling = StringMarshalling.Utf16)]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool VerQueryValue(byte[] pBlock, string pSubBlock, out string pValue, out uint len);

		[LibraryImport(winmm, EntryPoint = "waveOutGetVolume")]
		internal static partial uint waveOutGetVolume(nint hwo, out uint dwVolume);

		[LibraryImport(winmm, EntryPoint = "waveOutSetVolume")]
		internal static partial int waveOutSetVolume(nint hwo, uint dwVolume);

		[LibraryImport(user32, EntryPoint = "WindowFromPoint")]
		internal static partial nint WindowFromPoint(POINT Point);

		[LibraryImport(kernel32, EntryPoint = "WritePrivateProfileStringW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool WritePrivateProfileString(string lpAppName, string lpKeyName, string lpString, string lpFileName);

		[LibraryImport(kernel32, EntryPoint = "WritePrivateProfileSectionW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool WritePrivateProfileSection(string lpAppName, string lpString, string lpFileName);

		[LibraryImport(user32, EntryPoint = "SendMessageW", StringMarshalling = StringMarshalling.Utf16)]
		internal static partial int SendMessage(nint hwnd, uint msg, int wParam, [Out] char[] chars);

		[LibraryImport(user32, EntryPoint = "GetAncestor")]
		internal static partial nint GetAncestor(nint hwnd, GetAncestorFlags flags);

		[LibraryImport(user32, EntryPoint = "RegisterHotKey")]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool RegisterHotKey(nint hWnd, uint id, KeyModifiers fsModifiers, uint vk);

		[LibraryImport(user32, EntryPoint = "SetTimer")]
		internal static partial nuint SetTimer(nint hWnd, nuint nIDEvent, uint uElapse, TimerProc lpTimerFunc);

		[LibraryImport(user32, EntryPoint = "KillTimer")]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool KillTimer(nint hWnd, nuint uIDEvent);

		[LibraryImport(user32, EntryPoint = "EndDialog")]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool EndDialog(nint hDlg, nint nResult);

		[LibraryImport(user32, EntryPoint = "UnregisterHotKey")]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool UnregisterHotKey(nint hWnd, uint id);

		[LibraryImport(user32, EntryPoint = "IsCharAlphaNumericW")]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool IsCharAlphaNumeric([MarshalAs(UnmanagedType.U2)] char ch);

		/// <summary>
		/// Returns the first ancestor of aWnd that isn't itself a child.  aWnd itself is returned if
		/// it is not a child.  Returns NULL only if aWnd is NULL.  Also, it should always succeed
		/// based on the axiom that any window with the WS_CHILD style (aka WS_CHILDWINDOW) must have
		/// a non-child ancestor somewhere up the line.
		/// This function doesn't do anything special with owned vs. unowned windows.  Despite what MSDN
		/// says, GetParent() does not return the owner window, at least in some cases on Windows XP
		/// (e.g. BulletProof FTP Server). It returns NULL instead. In any case, it seems best not to
		/// worry about owner windows for this function's caller (MouseGetPos()), since it might be
		/// desirable for that command to return the owner window even though it can't actually be
		/// activated.  This is because attempts to activate an owner window should automatically cause
		/// the OS to activate the topmost owned window instead.  In addition, the owner window may
		/// contain the actual title or text that the user is interested in.  UPDATE: Due to the fact
		/// that this function retrieves the first parent that's not a child window, it's likely that
		/// that window isn't its owner anyway (since the owner problem usually applies to a parent
		/// window being owned by some controlling window behind it).
		/// </summary>
		/// <param name="hwnd"></param>
		/// <returns></returns>
		internal static nint GetNonChildParent(nint hwnd)
		{
			if (hwnd == 0) return hwnd;

			nint parent, parent_prev;

			for (parent_prev = hwnd; ; parent_prev = parent)
			{
				if ((GetWindowLongPtr(parent_prev, GWL_STYLE).ToInt64() & WS_CHILD) == 0)  // Found the first non-child parent, so return it.
					return parent_prev;

				parent = GetAncestor(parent_prev, gaFlags.GA_PARENT);

				if (parent == 0)
					return parent_prev;  // This will return aWnd if aWnd has no parents.
			}
		}

		internal static string GetWindowTextTimeout(nint hwnd, uint timeout)
		{
			_ = SendMessageTimeout(hwnd, WM_GETTEXTLENGTH, 0, 0, SendMessageTimeoutFlags.SMTO_ABORTIFHUNG, timeout, out var length);

			if (length == 0)
				return DefaultErrorString;

			var val = length.ToInt32();

			if (val == 0)
				return DefaultErrorString;

			var buffer = new char[val + 1];  // leave room for null-terminator
			nint ptr = 0;

			if (SendMessageTimeout(hwnd, WM_GETTEXT, buffer.Length, buffer, SendMessageTimeoutFlags.SMTO_ABORTIFHUNG, timeout, out ptr) == 0)
				return DefaultErrorString;

			return new string(buffer, 0, unchecked((int)ptr));
		}

		internal static bool ControlSetTab(nint hwnd, int tabIndex)
		{
			// MSDN: "If the tab control does not have the TCS_BUTTONS style, changing the focus also changes
			// the selected tab. In this case, the tab control sends the TCN_SELCHANGING and TCN_SELCHANGE
			// notification codes to its parent window."
			if (SendMessageTimeout(hwnd, TCM_SETCURFOCUS, tabIndex, 0, SendMessageTimeoutFlags.SMTO_ABORTIFHUNG, 2000, out var result) == 0)
				return false;

			// Tab controls with the TCS_BUTTONS style need additional work:
			if ((GetWindowLongPtr(hwnd, GWL_STYLE).ToInt64() & TCS_BUTTONS) != 0)
			{
				// Problem:
				//  TCM_SETCURFOCUS does not change the selected tab if TCS_BUTTONS is set.
				//
				// False solution #1 (which used to be recommended in the docs):
				//  Send a TCM_SETCURSEL method afterward.  TCM_SETCURSEL changes the selected tab,
				//  but doesn't notify the control's parent, so it doesn't update the tab's contents.
				//
				// False solution #2:
				//  Send a WM_NOTIFY message to the parent window to notify it.  Can't be done.
				//  MSDN says: "For Windows 2000 and later systems, the WM_NOTIFY message cannot
				//  be sent between processes."
				//
				// Solution #1:
				//  Send VK_LEFT/VK_RIGHT as many times as needed.
				//
				// Solution #2:
				//  Set the focus to an adjacent tab and then send VK_LEFT/VK_RIGHT.
				//   - Must choose an appropriate tab index and vk depending on which tab is being
				//     selected, since VK_LEFT/VK_RIGHT don't wrap around.
				//   - Ends up tempting optimisations which increase code size, such as to avoid
				//     TCM_SETCURFOCUS if an adjacent tab is already focused.
				//   - Still needs VK_SPACE afterward to actually select the tab.
				//
				// Solution #3 (the one below):
				//  Set the focus to the appropriate tab and then send VK_SPACE.
				//   - Since we've already set the focus, all we need to do is send VK_SPACE.
				//   - If the tab index is invalid and the user has focused but not selected
				//     another tab, that tab will be selected.  This seems harmless enough.
				//
				_ = PostMessage(hwnd, WM_KEYDOWN, VirtualKeys.VK_SPACE, 0x00000001);
				_ = PostMessage(hwnd, WM_KEYUP, VirtualKeys.VK_SPACE, 0xC0000001);
			}

			return true;
		}

		[LibraryImport(kernel32, EntryPoint = "GetStringTypeExW", StringMarshalling = StringMarshalling.Utf16)]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool GetStringTypeEx(uint Locale, uint dwInfoType, string lpSrcStr, int cchSrc, [Out] ushort[] lpCharType);

		[LibraryImport(kernel32, EntryPoint = "GetExitCodeThread")]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool GetExitCodeThread(nint hThread, out uint lpExitCode);

		[LibraryImport(kernel32, EntryPoint = "SetThreadPriority")]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool SetThreadPriority(nint hThread, int priority);

		[LibraryImport(kernel32, EntryPoint = "OpenProcess")]
		internal static partial nint OpenProcess(ProcessAccessTypes desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

		[LibraryImport(kernel32, EntryPoint = "QueryFullProcessImageNameW", StringMarshalling = StringMarshalling.Utf16)]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static unsafe partial bool QueryFullProcessImageName(nint hProcess, uint dwFlags, [Out] char[] lpExeName, ref uint lpdwSize);

		[LibraryImport(kernel32, EntryPoint = "VirtualAlloc")]
		internal static partial nint VirtualAlloc(nint lpAddress, nint dwSize, uint flAllocationType, uint flProtect);

		[LibraryImport(kernel32, EntryPoint = "VirtualFree")]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool VirtualFree(nint lpAddress, nint dwSize, uint dwFreeType);

		[LibraryImport(kernel32, EntryPoint = "VirtualAllocEx")]
		internal static partial nint VirtualAllocEx(nint hProcess, nint address, uint size, VirtualAllocExTypes allocationType, AccessProtectionFlags flags);

		[LibraryImport(kernel32, EntryPoint = "VirtualFreeEx")]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool VirtualFreeEx(nint hProcess, nint address, uint size, VirtualAllocExTypes dwFreeType);

		[LibraryImport(kernel32, EntryPoint = "ReadProcessMemory")]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool ReadProcessMemory(nint hProcess, nint baseAddress, byte[] buffer, uint dwSize, out uint numberOfBytesRead);

		[LibraryImport(kernel32, EntryPoint = "WriteProcessMemory")]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool WriteProcessMemory(nint hProcess, nint lpBaseAddress, nint lpBuffer, int nSize, out nint lpNumberOfBytesWritten);

		[StructLayout(LayoutKind.Sequential)]
		internal struct GdiplusStartupInputEx
		{
			public uint GdiplusVersion;
			public nint DebugEventCallback;
			[MarshalAs(UnmanagedType.Bool)] public bool SuppressBackgroundThread;
			[MarshalAs(UnmanagedType.Bool)] public bool SuppressExternalCodecs;
			public uint StartupParameters; // 0x4 at offset 24
			public uint _pad;
		}

		[DllImport(gdiplus)]
		internal static extern int GdiplusStartup(out nint token, ref GdiplusStartupInputEx input, nint output);

		[LibraryImport(gdi32, EntryPoint = "CreateEllipticRgn")]
		internal static partial nint CreateEllipticRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect);

		[LibraryImport(gdi32, EntryPoint = "CreateRoundRectRgn")]
		internal static partial nint CreateRoundRectRgn(int x1, int y1, int x2, int y2, int cx, int cy);

		[DllImport(gdi32, SetLastError = true)]
		internal static extern nint CreateCompatibleDC(nint hdc);

		[DllImport(gdi32, SetLastError = true)]
		internal static extern nint SelectObject(nint hdc, nint h);

		[DllImport(gdi32, SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static extern bool DeleteDC(nint hdc);

		[LibraryImport(gdi32, EntryPoint = "CreateRectRgn")]
		internal static partial nint CreateRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect);

		[LibraryImport(gdi32, EntryPoint = "CombineRgn")]
		internal static partial int CombineRgn(nint hrgnDest, nint hrgnSrc1, nint hrgnSrc2, int fnCombineMode);

		[LibraryImport(gdi32, EntryPoint = "CreatePolygonRgn")]
		internal static partial nint CreatePolygonRgn(POINT[] lppt, int cPoints, int fnPolyFillMode);

		[LibraryImport(user32, EntryPoint = "SetWindowRgn")]
		internal static partial int SetWindowRgn(nint hWnd, nint hRgn, [MarshalAs(UnmanagedType.Bool)] bool bRedraw);

		[LibraryImport(gdi32, EntryPoint = "DeleteObject")]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool DeleteObject(nint hObject);

		[DllImport(user32, CharSet = CharSet.Unicode, SetLastError = true)]
		internal static extern IntPtr LoadImage(nint hinst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

		[DllImport(user32, CharSet = CharSet.Unicode, SetLastError = true)]
		internal static extern uint PrivateExtractIcons(string lpszFile, int nIconIndex, int cxIcon, int cyIcon, [Out] nint[] phicon, [Out] uint[] piconid, uint nIcons, uint flags);

		internal delegate bool _EnumWindowsProc(nint hwnd, int lParam);//Add an underscore to this name because some sample programs use EnumWindowsProc as a function name.
		internal delegate void TimerProc(nint hWnd, uint uMsg, nuint idEvent, uint dwTime);

		internal delegate nint LowLevelKeyboardProc(int nCode, nint wParam, ref KBDLLHOOKSTRUCT lParam);

		internal delegate nint LowLevelMouseProc(int nCode, nint wParam, ref MSDLLHOOKSTRUCT lParam);

		internal delegate nint PlaybackProc(int nCode, nint wParam, ref EventMsg lParam);

		internal delegate void WinEventProc(nint hWinEventHook, uint eventType, nint hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

		[LibraryImport(user32, EntryPoint = "GetSystemMetrics")]
		internal static partial int GetSystemMetrics(SystemMetric smIndex);

		[LibraryImport(psapi, EntryPoint = "GetProcessImageFileNameW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
		internal static partial uint GetProcessImageFileName(nint hProcess, [Out] char[] lpExeName, uint nSize);

		[LibraryImport(kernel32, EntryPoint = "QueryDosDeviceW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
		internal static partial uint QueryDosDevice(string lpDeviceName, [Out] char[] lpTargetPath, uint ucchMax);

		[LibraryImport(combase)]
		internal static partial nint WindowsGetStringRawBuffer(nint hstr, out uint length);
		[LibraryImport(combase)]
		internal static partial int WindowsDeleteString(nint hstr);

		[ComImport]
		[Guid("00021401-0000-0000-C000-000000000046")]
		internal class ShellLink { }

		[ComImport]
		[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
		[Guid("000214F9-0000-0000-C000-000000000046")]
		internal interface IShellLinkW
		{
			void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile,
						int cchMaxPath,
						nint pfd,
						int fFlags);

			void GetIDList(out IntPtr ppidl);
			void SetIDList(IntPtr pidl);
			void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
			void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
			void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
			void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
			void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
			void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
			void GetHotkey(out short pwHotkey);
			void SetHotkey(short wHotkey);
			void GetShowCmd(out int piShowCmd);
			void SetShowCmd(int iShowCmd);
			void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath,
								int cchIconPath, out int piIcon);
			void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
			void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
			void Resolve(IntPtr hwnd, int fFlags);
			void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
		}
	}
}
#endif
