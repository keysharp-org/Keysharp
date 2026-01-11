#if LINUX
namespace Keysharp.Core.Linux.Proxies
{
	/// <summary>
	/// Proxy around a X11 xDisplay
	/// </summary>
	internal class XDisplay : IDisposable
	{
		[ThreadStatic]
		private static nint _defaultDisp = 0;
		private int screenNumber;
		internal nint WM_PROTOCOLS;
		internal nint WM_DELETE_WINDOW;
		internal nint WM_TAKE_FOCUS;
		//internal nint _NET_SUPPORTED;
		//internal nint _NET_CLIENT_LIST;
		//internal nint _NET_NUMBER_OF_DESKTOPS;
		internal nint _NET_DESKTOP_GEOMETRY;
		//internal nint _NET_DESKTOP_VIEWPORT;
		internal nint _NET_CURRENT_DESKTOP;
		//internal nint _NET_DESKTOP_NAMES;
		internal nint _NET_ACTIVE_WINDOW;
		internal nint _NET_WORKAREA;
		//internal nint _NET_SUPPORTING_WM_CHECK;
		//internal nint _NET_VIRTUAL_ROOTS;
		//internal nint _NET_DESKTOP_LAYOUT;
		//internal nint _NET_SHOWING_DESKTOP;
		//internal nint _NET_CLOSE_WINDOW;
		//internal nint _NET_MOVERESIZE_WINDOW;
		internal nint _NET_WM_MOVERESIZE;
		//internal nint _NET_RESTACK_WINDOW;
		//internal nint _NET_REQUEST_FRAME_EXTENTS;
		internal nint _NET_WM_NAME;
		//internal nint _NET_WM_VISIBLE_NAME;
		//internal nint _NET_WM_ICON_NAME;
		//internal nint _NET_WM_VISIBLE_ICON_NAME;
		//internal nint _NET_WM_DESKTOP;
		internal nint _NET_WM_WINDOW_TYPE;
		internal nint _NET_WM_STATE;
		//internal nint _NET_WM_ALLOWED_ACTIONS;
		//internal nint _NET_WM_STRUT;
		//internal nint _NET_WM_STRUT_PARTIAL;
		//internal nint _NET_WM_ICON_GEOMETRY;
		internal nint _NET_WM_ICON;
		internal nint _NET_WM_PID;
		//internal nint _NET_WM_HANDLED_ICONS;
		internal nint _NET_WM_USER_TIME;
		internal nint _NET_FRAME_EXTENTS;
		//internal nint _NET_WM_PING;
		//internal nint _NET_WM_SYNC_REQUEST;
		internal nint _NET_SYSTEM_TRAY_S;
		//internal nint _NET_SYSTEM_TRAY_ORIENTATION;
		internal nint _NET_SYSTEM_TRAY_OPCODE;
		internal nint _NET_WM_STATE_MAXIMIZED_HORZ;
		internal nint _NET_WM_STATE_MAXIMIZED_VERT;
		internal nint _XEMBED;
		internal nint _XEMBED_INFO;
		internal nint _MOTIF_WM_HINTS;
		internal nint _NET_WM_STATE_SKIP_TASKBAR;
		internal nint _NET_WM_STATE_ABOVE;
		internal nint _NET_WM_STATE_MODAL;
		internal nint _NET_WM_STATE_HIDDEN;
		internal nint _NET_WM_CONTEXT_HELP;
		internal nint _NET_WM_WINDOW_OPACITY;
		//internal nint _NET_WM_WINDOW_TYPE_DESKTOP;
		//internal nint _NET_WM_WINDOW_TYPE_DOCK;
		//internal nint _NET_WM_WINDOW_TYPE_TOOLBAR;
		//internal nint _NET_WM_WINDOW_TYPE_MENU;
		internal nint _NET_WM_WINDOW_TYPE_UTILITY;
		//internal nint _NET_WM_WINDOW_TYPE_SPLASH;
		// internal nint _NET_WM_WINDOW_TYPE_DIALOG;
		internal nint _NET_WM_WINDOW_TYPE_NORMAL;
		internal nint CLIPBOARD;
		internal nint PRIMARY;
		//internal nint DIB;
		internal nint OEMTEXT;
		internal nint UTF8_STRING;
		internal nint UTF16_STRING;
		internal nint RICHTEXTFORMAT;
		internal nint TARGETS;
		internal nint PostAtom;       // PostMessage atom
		internal nint AsyncAtom;      // Support for async messages
		internal nint HoverAtom;       // PostMessage atom

		[ThreadStatic] // According to StackOverflow, each thread should have its own XDisplay instance.
		private static XDisplay _default = null;
		internal static XDisplay Default
		{
			get
			{
				if (_defaultDisp == 0)
				{
					_defaultDisp = Xlib.XOpenDisplay(0);
					_default = new XDisplay(_defaultDisp);
				}

				return _default;
			}
		}

		internal nint Handle { get; private set; } = 0;

		internal XWindow Root => new XWindow(this, Xlib.XDefaultRootWindow(Handle));

		internal int ScreenNumber => screenNumber;

		internal XDisplay(nint prt)
		{
			Handle = prt;
			screenNumber = Xlib.XDefaultScreen(Handle);
			SetupAtoms();
		}

		~XDisplay()
		{
			Dispose();
		}

		public void Dispose()
		{
			if (Handle != 0 && Handle != _defaultDisp) 
			{
				Xlib.XCloseDisplay(Handle);
				Handle = 0;
			}
		}

		/// <summary>
		/// Returns the window which currently has input focus
		/// </summary>
		/// <returns></returns>
		internal XWindow XGetInputFocusWindow()
		{
			_ = Xlib.XGetInputFocus(Handle, out var hwndWnd, out var focusState);
			return new XWindow(this, hwndWnd);
		}

		/// <summary>
		/// Returns the handle of the window which currently has input focus
		/// </summary>
		/// <returns></returns>
		internal long XGetInputFocusHandle()
		{
			_ = Xlib.XGetInputFocus(Handle, out var hwndWnd, out var focusState);
			return hwndWnd;
		}

		/// <summary>
		/// Returns all Windows of this XDisplay
		/// </summary>
		/// <returns></returns>
		internal IEnumerable<XWindow> XQueryTree(Func<long, bool> filter = null) => XQueryTree(Root, filter);

		/// <summary>
		/// Return all child xWindows from given xWindow
		/// </summary>
		/// <param name="windowToObtain"></param>
		/// <returns></returns>
		internal unsafe IEnumerable<XWindow> XQueryTree(XWindow windowToObtain, Func<long, bool> filter = null)
		{
			var childrenReturn = nint.Zero;
			var windows = new List<XWindow>();

			try
			{
				if (Xlib.XQueryTree(Handle, windowToObtain.ID, out var rootReturn, out var parentReturn, out childrenReturn, out var nChildrenReturn) != 0)
				{
					var pSource = (long*)childrenReturn.ToPointer();

					for (var i = 0; i < nChildrenReturn; i++)
					{
						try
						{
							var id = pSource[i];

							if (filter == null || filter(id))
							{
								var window = new XWindow(this, id);
								windows.Add(window);
								//var tempItem = new WindowItem(window);
								//KeysharpEnhancements.OutputDebugLine($"Adding window from XQueryTree() with id: {id}, title: {tempItem.Title}");
							}
						}
						catch (Exception ex)
						{
							KeysharpEnhancements.OutputDebugLine($"Error when applying XQueryTree() filter: {ex.Message}");
						}
					}
				}
			}
			catch (Exception e)
			{
				KeysharpEnhancements.OutputDebugLine(e.Message);
			}
			finally
			{
				if (childrenReturn != 0)
					_ = Xlib.XFree(childrenReturn);
			}

			//KeysharpEnhancements.OutputDebugLine($"Exiting XQueryTree().");
			return windows;
		}

		/// <summary>
		/// Return all child xWindows from given xWindow, recursively.
		/// </summary>
		/// <param name="windowToObtain"></param>
		/// <returns></returns>
		internal unsafe IEnumerable<XWindow> XQueryTreeRecursive(Func<long, bool> filter = null) => XQueryTreeRecursive(Root, filter);
		internal unsafe IEnumerable<XWindow> XQueryTreeRecursive(XWindow windowToObtain, Func<long, bool> filter = null)
		{
			var childrenReturn = nint.Zero;
			var windows = new HashSet<XWindow>();

			try
			{
				if (Xlib.XQueryTree(Handle, windowToObtain.ID, out var rootReturn, out var parentReturn, out childrenReturn, out var nChildrenReturn) != 0)
				{
					var pSource = (long*)childrenReturn.ToPointer();

					for (var i = 0; i < nChildrenReturn; i++)
					{
						var id = pSource[i];

						//We are assuming that if this window didn't pass the filter test, then its child windows won't either.
						if (filter == null || filter(id))
						{
							var window = new XWindow(this, id);
							windows.Add(window);
							//var tempItem = new WindowItem(window);
							//KeysharpEnhancements.OutputDebugLine($"Adding window from XQueryTree() with id: {id}, title: {tempItem.Title}");
							windows.AddRange(XQueryTreeRecursive(window, filter));
						}
					}
				}
			}
			catch (Exception e)
			{
				KeysharpEnhancements.OutputDebugLine(e.Message);
			}
			finally
			{
				if (childrenReturn != 0)
					_ = Xlib.XFree(childrenReturn);
			}

			//KeysharpEnhancements.OutputDebugLine($"Exiting XQueryTreeRecursive().");
			return windows;
		}

		internal void SetupAtoms()
		{
			// make sure this array stays in sync with the statements below
			string[] atom_names = new string[]
			{
				"WM_PROTOCOLS",
				"WM_DELETE_WINDOW",
				"WM_TAKE_FOCUS",
				//"_NET_SUPPORTED",
				//"_NET_CLIENT_LIST",
				//"_NET_NUMBER_OF_DESKTOPS",
				"_NET_DESKTOP_GEOMETRY",
				//"_NET_DESKTOP_VIEWPORT",
				"_NET_CURRENT_DESKTOP",
				//"_NET_DESKTOP_NAMES",
				"_NET_ACTIVE_WINDOW",
				"_NET_WORKAREA",
				//"_NET_SUPPORTING_WM_CHECK",
				//"_NET_VIRTUAL_ROOTS",
				//"_NET_DESKTOP_LAYOUT",
				//"_NET_SHOWING_DESKTOP",
				//"_NET_CLOSE_WINDOW",
				//"_NET_MOVERESIZE_WINDOW",
				"_NET_WM_MOVERESIZE",
				//"_NET_RESTACK_WINDOW",
				//"_NET_REQUEST_FRAME_EXTENTS",
				"_NET_WM_NAME",
				//"_NET_WM_VISIBLE_NAME",
				//"_NET_WM_ICON_NAME",
				//"_NET_WM_VISIBLE_ICON_NAME",
				//"_NET_WM_DESKTOP",
				"_NET_WM_WINDOW_TYPE",
				"_NET_WM_STATE",
				//"_NET_WM_ALLOWED_ACTIONS",
				//"_NET_WM_STRUT",
				//"_NET_WM_STRUT_PARTIAL",
				//"_NET_WM_ICON_GEOMETRY",
				"_NET_WM_ICON",
				"_NET_WM_PID",
				//"_NET_WM_HANDLED_ICONS",
				"_NET_WM_USER_TIME",
				"_NET_FRAME_EXTENTS",
				//"_NET_WM_PING",
				//"_NET_WM_SYNC_REQUEST",
				"_NET_SYSTEM_TRAY_OPCODE",
				//"_NET_SYSTEM_TRAY_ORIENTATION",
				"_NET_WM_STATE_MAXIMIZED_HORZ",
				"_NET_WM_STATE_MAXIMIZED_VERT",
				"_NET_WM_STATE_HIDDEN",
				"_XEMBED",
				"_XEMBED_INFO",
				"_MOTIF_WM_HINTS",
				"_NET_WM_STATE_SKIP_TASKBAR",
				"_NET_WM_STATE_ABOVE",
				"_NET_WM_STATE_MODAL",
				"_NET_WM_CONTEXT_HELP",
				"_NET_WM_WINDOW_OPACITY",
				//"_NET_WM_WINDOW_TYPE_DESKTOP",
				//"_NET_WM_WINDOW_TYPE_DOCK",
				//"_NET_WM_WINDOW_TYPE_TOOLBAR",
				//"_NET_WM_WINDOW_TYPE_MENU",
				"_NET_WM_WINDOW_TYPE_UTILITY",
				// "_NET_WM_WINDOW_TYPE_DIALOG",
				//"_NET_WM_WINDOW_TYPE_SPLASH",
				"_NET_WM_WINDOW_TYPE_NORMAL",
				"CLIPBOARD",
				"PRIMARY",
				"COMPOUND_TEXT",
				"UTF8_STRING",
				"UTF16_STRING",
				"RICHTEXTFORMAT",
				"TARGETS",
				"_SWF_AsyncAtom",
				"_SWF_PostMessageAtom",
				"_SWF_HoverAtom"
			};
			nint[] atoms = new nint[atom_names.Length];
			_ = Xlib.XInternAtoms(Handle, atom_names, atom_names.Length, false, atoms);
			int off = 0;
			WM_PROTOCOLS = atoms[off++];
			WM_DELETE_WINDOW = atoms[off++];
			WM_TAKE_FOCUS = atoms[off++];
			//_NET_SUPPORTED = atoms [off++];
			//_NET_CLIENT_LIST = atoms [off++];
			//_NET_NUMBER_OF_DESKTOPS = atoms [off++];
			_NET_DESKTOP_GEOMETRY = atoms[off++];
			//_NET_DESKTOP_VIEWPORT = atoms [off++];
			_NET_CURRENT_DESKTOP = atoms[off++];
			//_NET_DESKTOP_NAMES = atoms [off++];
			_NET_ACTIVE_WINDOW = atoms[off++];
			_NET_WORKAREA = atoms[off++];
			//_NET_SUPPORTING_WM_CHECK = atoms [off++];
			//_NET_VIRTUAL_ROOTS = atoms [off++];
			//_NET_DESKTOP_LAYOUT = atoms [off++];
			//_NET_SHOWING_DESKTOP = atoms [off++];
			//_NET_CLOSE_WINDOW = atoms [off++];
			//_NET_MOVERESIZE_WINDOW = atoms [off++];
			_NET_WM_MOVERESIZE = atoms[off++];
			//_NET_RESTACK_WINDOW = atoms [off++];
			//_NET_REQUEST_FRAME_EXTENTS = atoms [off++];
			_NET_WM_NAME = atoms[off++];
			//_NET_WM_VISIBLE_NAME = atoms [off++];
			//_NET_WM_ICON_NAME = atoms [off++];
			//_NET_WM_VISIBLE_ICON_NAME = atoms [off++];
			//_NET_WM_DESKTOP = atoms [off++];
			_NET_WM_WINDOW_TYPE = atoms[off++];
			_NET_WM_STATE = atoms[off++];
			//_NET_WM_ALLOWED_ACTIONS = atoms [off++];
			//_NET_WM_STRUT = atoms [off++];
			//_NET_WM_STRUT_PARTIAL = atoms [off++];
			//_NET_WM_ICON_GEOMETRY = atoms [off++];
			_NET_WM_ICON = atoms[off++];
			_NET_WM_PID = atoms [off++];
			//_NET_WM_HANDLED_ICONS = atoms [off++];
			_NET_WM_USER_TIME = atoms[off++];
			_NET_FRAME_EXTENTS = atoms[off++];
			//_NET_WM_PING = atoms [off++];
			//_NET_WM_SYNC_REQUEST = atoms [off++];
			_NET_SYSTEM_TRAY_OPCODE = atoms[off++];
			//_NET_SYSTEM_TRAY_ORIENTATION = atoms [off++];
			_NET_WM_STATE_MAXIMIZED_HORZ = atoms[off++];
			_NET_WM_STATE_MAXIMIZED_VERT = atoms[off++];
			_NET_WM_STATE_HIDDEN = atoms[off++];
			_XEMBED = atoms[off++];
			_XEMBED_INFO = atoms[off++];
			_MOTIF_WM_HINTS = atoms[off++];
			_NET_WM_STATE_SKIP_TASKBAR = atoms[off++];
			_NET_WM_STATE_ABOVE = atoms[off++];
			_NET_WM_STATE_MODAL = atoms[off++];
			_NET_WM_CONTEXT_HELP = atoms[off++];
			_NET_WM_WINDOW_OPACITY = atoms[off++];
			//_NET_WM_WINDOW_TYPE_DESKTOP = atoms [off++];
			//_NET_WM_WINDOW_TYPE_DOCK = atoms [off++];
			//_NET_WM_WINDOW_TYPE_TOOLBAR = atoms [off++];
			//_NET_WM_WINDOW_TYPE_MENU = atoms [off++];
			_NET_WM_WINDOW_TYPE_UTILITY = atoms[off++];
			// _NET_WM_WINDOW_TYPE_DIALOG = atoms [off++];
			//_NET_WM_WINDOW_TYPE_SPLASH = atoms [off++];
			_NET_WM_WINDOW_TYPE_NORMAL = atoms[off++];
			CLIPBOARD = atoms[off++];
			PRIMARY = atoms[off++];
			OEMTEXT = atoms[off++];
			UTF8_STRING = atoms[off++];
			UTF16_STRING = atoms[off++];
			RICHTEXTFORMAT = atoms[off++];
			TARGETS = atoms[off++];
			AsyncAtom = atoms[off++];
			PostAtom = atoms[off++];
			HoverAtom = atoms[off++];
			//DIB = (nint)Atom.XA_PIXMAP;
			_NET_SYSTEM_TRAY_S = Xlib.XInternAtom(Handle, "_NET_SYSTEM_TRAY_S" + screenNumber.ToString(), false);
			//for (var i = 0; i < atom_names.Length; i++)
			//  KeysharpEnhancements.OutputDebugLine($"Atom {atom_names[i]} = {atoms[i].ToInt64()}.");
		}


		internal uint XKeycodeToKeysym(int keycode, int index) => Xlib.XKeycodeToKeysym(Handle, keycode, index);
		internal uint XKeysymToKeycode(IntPtr keysym) => Xlib.XKeysymToKeycode(Handle, keysym);
		internal int XQueryKeymap(byte[] keys_return) => Xlib.XQueryKeymap(Handle, keys_return);
		internal int XGrabKey(uint keycode, uint modifiers, nint grab_window, bool owner_events, int pointer_mode, int keyboard_mode)
			=> Xlib.XGrabKey(Handle, keycode, modifiers, grab_window, owner_events, pointer_mode, keyboard_mode);
		internal int XUngrabKey(uint keycode, uint modifiers, long? grab_window = default)
			=> Xlib.XUngrabKey(Handle, keycode, modifiers, (nint)(grab_window ?? Root.ID));
		internal int XUngrabKeyboard(ulong time) => Xlib.XUngrabKeyboard(Handle, time);
		internal int XSync(bool discard) => Xlib.XSync(Handle, discard);
		internal int XFlush() => Xlib.XFlush(Handle);
		internal bool XTestFakeKeyEvent(uint keycode, bool isPress, ulong delay) => Xlib.XTestFakeKeyEvent(Handle, keycode, isPress, delay);
	}
}
#endif