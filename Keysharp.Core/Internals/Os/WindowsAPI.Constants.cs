namespace Keysharp.Internals.Os.Windows
{
	public static partial class WindowsAPI
	{
		// File / Device IO
		internal const uint GENERICREAD = 0x80000000;

		internal const int GW_HWNDFIRST = 0;
		internal const int GW_HWNDLAST = 1;
		internal const int GW_HWNDNEXT = 2;
		internal const int GW_HWNDPREV = 3;
		internal const int GW_OWNER = 4;
		internal const int GW_CHILD = 5;
		internal const int GW_ENABLEDPOPUP = 6;
		internal const int GW_MAX = 6;

		internal const int GWL_EXSTYLE = -20;

		internal const int GWL_STYLE = -16;

		internal const int GWL_WNDPROC = -4;
		internal const int DWLP_MSGRESULT = 0;

		internal const int HWND_BOTTOM = 1;

		internal const int HWND_BROADCAST = 0xffff;

		internal const int HWND_NOTOPMOST = -2;

		internal const int HWND_TOP = 0;

		internal const int HWND_TOPMOST = -1;

		internal const int INPUT_HARDWARE = 2;

		internal const int INPUT_KEYBOARD = 1;

		internal const int INPUT_MOUSE = 0;

		internal const int INVALID_HANDLE = -1;

		internal const uint IOCTL_STORAGE_EJECT_MEDIA = 2967560;

		internal const uint IOCTL_STORAGE_EJECTION_CONTROL = 0x2d0940;

		internal const uint IOCTL_STORAGE_LOAD_MEDIA = 0x2d480c;

		internal const int LWA_ALPHA = 0x2;

		internal const int LWA_COLORKEY = 0x1;

		internal const byte AC_SRC_OVER = 0x00;

		internal const byte AC_SRC_ALPHA = 0x01;

		internal const uint ULW_ALPHA = 0x00000002;

		internal const int MAPVK_VK_TO_VSC = 0;
		internal const int MAPVK_VSC_TO_VK = 1;
		internal const int MAPVK_VK_TO_CHAR = 2;
		internal const int MAPVK_VSC_TO_VK_EX = 3;
		internal const int MAPVK_VK_TO_VSC_EX = 4;

		internal const int MF_BYCOMMAND = 0;

		internal const int MF_BYPOSITION = 0x400;

		internal const uint OPENEXISTING = 3;

		internal const int PROCESS_ALL_ACCESS = STANDARD_RIGHTS_REQUIRED | SYNCHRONIZE | 0xFFF;

		internal const int SC_CLOSE = 0xF060;

		internal const int SHERB_NOCONFIRMATION = 0x1;

		internal const int SHERB_NOPROGRESSUI = 0x2;

		internal const int SHERB_NOSOUND = 0x4;

		internal const int STANDARD_RIGHTS_REQUIRED = 0x000F0000;

		internal const int SW_FORCEMINIMIZE = 11;

		internal const int SW_HIDE = 0;

		internal const int SW_MAX = 11;

		internal const int SW_MAXIMIZE = 3;

		internal const int SW_MINIMIZE = 6;

		internal const int SW_NORMAL = 1;

		internal const int SW_RESTORE = 9;

		internal const int SW_SHOW = 5;

		internal const int SW_SHOWDEFAULT = 10;

		internal const int SW_SHOWMAXIMIZED = 3;

		internal const int SW_SHOWMINIMIZED = 2;

		internal const int SW_SHOWMINNOACTIVE = 7;

		internal const int SW_SHOWNA = 8;

		internal const int SW_SHOWNOACTIVATE = 4;

		internal const int SW_SHOWNORMAL = 1;

		internal const int SWP_NOACTIVATE = 0x10;

		internal const int SWP_FRAMECHANGED = 0x0020;

		internal const int SWP_NOMOVE = 2;

		internal const int SWP_NOSIZE = 1;

		internal const int SWP_NOZORDER = 0x0004;

		internal const int SYNCHRONIZE = 0x00100000;

		internal const int LV_REMOTE_BUF_SIZE = 1024;// 8192 (below) seems too large in hindsight, given that an LV can only display the first 260 chars in a field.
		internal const int LV_TEXT_BUF_SIZE = 8192;// Max amount of text in a ListView sub-item.  Somewhat arbitrary: not sure what the real limit is, if any.

		internal const int WM_HOTKEY = 0x0312;
		internal const int WM_KEYDOWN = 0x0100;
		internal const int WM_KEYUP = 0x0101;
		internal const int WM_SYSKEYDOWN = 0x0104;
		internal const int WM_SYSKEYUP = 0x0105;
		internal const int WM_CLOSE = 0x0010;
		internal const int WM_QUIT = 0x0012;
		internal const int WM_GETMINMAXINFO = 0x0024;
		internal const int WM_VKEYTOITEM = 0x002E;
		internal const int WM_CHARTOITEM = 0x002F;
		internal const int WM_QUERYDRAGICON = 0x0037;
		internal const int WM_COMPAREITEM = 0x0039;
		internal const int WM_SETICON = 0x0080;
		internal const int WM_SIZE = 0x0005;
		internal const int WM_COMMAND = 0x0111;
		internal const int WM_SETREDRAW = 0x000B;
		internal const int WM_SETTEXT = 0x000C;
		internal const int WM_GETTEXT = 0x000D;
		internal const int WM_GETTEXTLENGTH = 0x000E;
		internal const int WM_SETTINGCHANGE = 0x001A;
		internal const int WM_SYSCOMMAND = 0x0112;
		internal const int WM_HSCROLL = 0x0114;
		internal const int WM_VSCROLL = 0x0115;
		internal const int WM_USER = 0x0400;
		internal const int WM_KEYFIRST = 0x0100;
		internal const int WM_CHAR = 0x0102;
		internal const int WM_DEADCHAR = 0x0103;
		internal const int WM_SYSCHAR = 0x0106;
		internal const int WM_SYSDEADCHAR = 0x0107;
		internal const int WM_UNICHAR = 0x0109;
		internal const int WM_KEYLAST = 0x0109;
		internal const int WM_MOUSEFIRST = 0x0200;
		internal const int WM_MOUSEMOVE = 0x0200;
		internal const int WM_LBUTTONDOWN = 0x0201;
		internal const int WM_LBUTTONUP = 0x0202;
		internal const int WM_LBUTTONDBLCLK = 0x0203;
		internal const int WM_RBUTTONDOWN = 0x0204;
		internal const int WM_RBUTTONUP = 0x0205;
		internal const int WM_RBUTTONDBLCLK = 0x0206;
		internal const int WM_MBUTTONDOWN = 0x0207;
		internal const int WM_MBUTTONUP = 0x0208;
		internal const int WM_MBUTTONDBLCLK = 0x0209;
		internal const int WM_MOUSEWHEEL = 0x020A;
		internal const int WM_MOUSEHWHEEL = 0x020E;
		internal const int WM_XBUTTONDOWN = 0x020B;
		internal const int WM_NCXBUTTONDOWN = 0x00AB;
		internal const int WM_XBUTTONUP = 0x020C;
		internal const int WM_NCXBUTTONUP = 0x00AC;
		internal const int WM_XBUTTONDBLCLK = 0x020D;
		internal const int WM_MOUSELAST = 0x020E;
		internal const int WM_CLIPBOARDUPDATE = 0x031D;
		internal const int WM_NCCREATE = 0x0081;
		internal const int WM_NCDESTROY = 0x0082;
		internal const int WM_NCCALCSIZE = 0x0083;
		internal const int WM_NCHITTEST = 0x0084;
		internal const int WM_NCPAINT = 0x0085;
		internal const int WM_NCACTIVATE = 0x0086;
		internal const int WM_GETDLGCODE = 0x0087;
		internal const int WM_ENDSESSION = 0x0016;
		internal const int WM_ERASEBKGND = 0x0014;
		internal const int WM_CTLCOLORMSGBOX = 0x0132;
		internal const int WM_CTLCOLOREDIT = 0x0133;
		internal const int WM_CTLCOLORLISTBOX = 0x0134;
		internal const int WM_CTLCOLORBTN = 0x0135;
		internal const int WM_CTLCOLORDLG = 0x0136;
		internal const int WM_CTLCOLORSCROLLBAR = 0x0137;
		internal const int WM_CTLCOLORSTATIC = 0x0138;
		internal const uint WM_THEMECHANGED = 0x031A;
		internal const int WM_COMMNOTIFY = 0x0044; // Used like AHK's WM_COMMNOTIFY to deliver pre-dialog notifications to OnMessage handlers.
		internal const int WM_INITDIALOG = 0x0110;
		internal const int WM_DESTROY = 0x0002;
		internal const int WM_COPYDATA = 0x004A;
		internal const int WM_PAINT = 0x000F;
		internal const int WM_GETFONT = 0x0031;
		internal const int WM_SETFONT = 0x0030;


		internal const uint ENDSESSION_LOGOFF = 0x80000000;

		internal const int HTERROR = -2;
		internal const int HTTRANSPARENT = -1;
		internal const int HTNOWHERE = 0;
		internal const int HTCLIENT = 1;
		internal const int HTCAPTION = 2;
		internal const int HTSYSMENU = 3;
		internal const int HTGROWBOX = 4;
		internal const int HTSIZE = HTGROWBOX;
		internal const int HTMENU = 5;
		internal const int HTHSCROLL = 6;
		internal const int HTVSCROLL = 7;
		internal const int HTMINBUTTON = 8;
		internal const int HTMAXBUTTON = 9;
		internal const int HTLEFT = 10;
		internal const int HTRIGHT = 11;
		internal const int HTTOP = 12;
		internal const int HTTOPLEFT = 13;
		internal const int HTTOPRIGHT = 14;
		internal const int HTBOTTOM = 15;
		internal const int HTBOTTOMLEFT = 16;
		internal const int HTBOTTOMRIGHT = 17;
		internal const int HTBORDER = 18;
		internal const int HTREDUCE = HTMINBUTTON;
		internal const int HTZOOM = HTMAXBUTTON;
		internal const int HTSIZEFIRST = HTLEFT;
		internal const int HTSIZELAST = HTBOTTOMRIGHT;
		internal const int HTOBJECT = 19;

		internal const int HTCLOSE = 20;
		internal const int HTHELP = 21;

		internal const uint PM_NOREMOVE = 0x0000;
		internal const uint PM_REMOVE = 0x0001;
		internal const uint PM_NOYIELD = 0x0002;

		internal const int MK_LBUTTON = 0x0001;
		internal const int MK_RBUTTON = 0x0002;
		internal const int MK_SHIFT = 0x0004;
		internal const int MK_CONTROL = 0x0008;
		internal const int MK_MBUTTON = 0x0010;
		internal const int MK_XBUTTON1 = 0x0020;
		internal const int MK_XBUTTON2 = 0x0040;

		internal const int HOTKEYF_SHIFT = 0x01;
		internal const int HOTKEYF_CONTROL = 0x02;
		internal const int HOTKEYF_ALT = 0x04;

		internal const int WS_EX_LAYERED = 0x80000;
		internal const int WS_EX_TRANSPARENT = 0x20;
		internal const int WS_EX_TOPMOST = 8;
		internal const int WS_EX_NOACTIVATE = 0x08000000;
		internal const int WS_EX_TOOLWINDOW = 0x00000080;
		internal const int WS_EX_APPWINDOW = 0x00040000;
		internal const int WS_OVERLAPPED = 0x00000000;
		internal const uint WS_POPUP = 0x80000000;
		internal const int WS_CHILD = 0x40000000;
		internal const int WS_MINIMIZE = 0x20000000;
		internal const int WS_VISIBLE = 0x10000000;
		internal const int WS_DISABLED = 0x08000000;
		internal const int WS_CLIPSIBLINGS = 0x04000000;
		internal const int WS_CLIPCHILDREN = 0x02000000;
		internal const int WS_MAXIMIZE = 0x01000000;
		internal const int WS_CAPTION = 0x00C00000;     /* WS_BORDER | WS_DLGFRAME  */
		internal const int WS_BORDER = 0x00800000;
		internal const int WS_DLGFRAME = 0x00400000;
		internal const int WS_VSCROLL = 0x00200000;
		internal const int WS_HSCROLL = 0x00100000;
		internal const int WS_SYSMENU = 0x00080000;
		internal const int WS_THICKFRAME = 0x00040000;
		internal const int WS_GROUP = 0x00020000;
		internal const int WS_TABSTOP = 0x00010000;
		internal const int WS_MINIMIZEBOX = 0x00020000;
		internal const int WS_MAXIMIZEBOX = 0x00010000;
		internal const int WS_TILED = WS_OVERLAPPED;
		internal const int WS_ICONIC = WS_MINIMIZE;
		internal const int WS_SIZEBOX = WS_THICKFRAME;

		internal const int ES_LEFT = 0x0000;
		internal const int ES_CENTER = 0x0001;
		internal const int ES_RIGHT = 0x0002;
		internal const int ES_MULTILINE = 0x0004;
		internal const int ES_UPPERCASE = 0x0008;
		internal const int ES_LOWERCASE = 0x0010;
		internal const int ES_PASSWORD = 0x0020;
		internal const int ES_AUTOVSCROLL = 0x0040;
		internal const int ES_AUTOHSCROLL = 0x0080;
		internal const int ES_NOHIDESEL = 0x0100;
		internal const int ES_OEMCONVERT = 0x0400;
		internal const int ES_READONLY = 0x0800;
		internal const int ES_WANTRETURN = 0x1000;
		internal const int ES_NUMBER = 0x2000;
		internal const int CBS_AUTOHSCROLL = 0x0040;
		internal const int BS_NOTIFY = 0x4000;
		internal const uint TBM_SETTHUMBLENGTH = WM_USER + 27;
		internal const uint TBM_SETTIPSIDE = WM_USER + 31;
		internal const int TBS_FIXEDLENGTH = 0x0040;
		internal const int TBS_TOOLTIPS = 0x0100;
		internal const int TVS_NOHSCROLL = 0x8000;
		internal const uint SB_GETTEXT = WM_USER + 13;
		internal const uint SB_SETPARTS = WM_USER + 4;
		internal const uint SB_GETPARTS = WM_USER + 6;
		internal const uint SB_GETTEXTLENGTH = WM_USER + 12;

		internal const int SB_LINEUP = 0;
		internal const int SB_LINELEFT = 0;
		internal const int SB_LINEDOWN = 1;
		internal const int SB_LINERIGHT = 1;
		internal const int SB_PAGEUP = 2;
		internal const int SB_PAGELEFT = 2;
		internal const int SB_PAGEDOWN = 3;
		internal const int SB_PAGERIGHT = 3;
		internal const int SB_THUMBPOSITION = 4;
		internal const int SB_THUMBTRACK = 5;
		internal const int SB_TOP = 6;
		internal const int SB_LEFT = 6;
		internal const int SB_BOTTOM = 7;
		internal const int SB_RIGHT = 7;
		internal const int SB_ENDSCROLL = 8;

		internal const int EM_GETSEL = 0x00B0;
		internal const int EM_SETSEL = 0x00B1;
		internal const int EM_GETRECT = 0x00B2;
		internal const int EM_SETRECT = 0x00B3;
		internal const int EM_SETRECTNP = 0x00B4;
		internal const int EM_SCROLL = 0x00B5;
		internal const int EM_LINESCROLL = 0x00B6;
		internal const int EM_SCROLLCARET = 0x00B7;
		internal const int EM_GETMODIFY = 0x00B8;
		internal const int EM_SETMODIFY = 0x00B9;
		internal const int EM_GETLINECOUNT = 0x00BA;
		internal const int EM_LINEINDEX = 0x00BB;
		internal const int EM_SETHANDLE = 0x00BC;
		internal const int EM_GETHANDLE = 0x00BD;
		internal const int EM_GETTHUMB = 0x00BE;
		internal const int EM_LINELENGTH = 0x00C1;
		internal const int EM_REPLACESEL = 0x00C2;
		internal const int EM_GETLINE = 0x00C4;
		internal const int EM_LIMITTEXT = 0x00C5;
		internal const int EM_CANUNDO = 0x00C6;
		internal const int EM_UNDO = 0x00C7;
		internal const int EM_FMTLINES = 0x00C8;
		internal const int EM_LINEFROMCHAR = 0x00C9;
		internal const int EM_SETTABSTOPS = 0x00CB;
		internal const int EM_SETPASSWORDCHAR = 0x00CC;
		internal const int EM_EMPTYUNDOBUFFER = 0x00CD;
		internal const int EM_GETFIRSTVISIBLELINE = 0x00CE;
		internal const int EM_SETREADONLY = 0x00CF;
		internal const int EM_SETWORDBREAKPROC = 0x00D0;
		internal const int EM_GETWORDBREAKPROC = 0x00D1;
		internal const int EM_GETPASSWORDCHAR = 0x00D2;
		internal const int EM_SETMARGINS = 0x00D3;
		internal const int EM_GETMARGINS = 0x00D4;
		internal const int EM_SETLIMITTEXT = EM_LIMITTEXT;   //win40 Name change
		internal const int EM_GETLIMITTEXT = 0x00D5;
		internal const int EM_POSFROMCHAR = 0x00D6;
		internal const int EM_CHARFROMPOS = 0x00D7;
		internal const int EM_SETIMESTATUS = 0x00D8;
		internal const int EM_GETIMESTATUS = 0x00D9;
		internal const int EM_ENABLEFEATURE = 0x00DA;
		internal const int EM_SETCUEBANNER = 0x1501;

		internal const int BM_GETCHECK = 0x00F0;
		internal const int BM_SETCHECK = 0x00F1;
		internal const int BM_GETSTATE = 0x00F2;
		internal const int BM_SETSTATE = 0x00F3;
		internal const int BM_SETSTYLE = 0x00F4;
		internal const int BM_CLICK = 0x00F5;
		internal const int BM_GETIMAGE = 0x00F6;
		internal const int BM_SETIMAGE = 0x00F7;
		internal const int BM_SETDONTCLICK = 0x00F8;
		internal const int BST_UNCHECKED = 0x0000;
		internal const int BST_CHECKED = 0x0001;
		internal const int BST_INDETERMINATE = 0x0002;
		internal const int BST_PUSHED = 0x0004;
		internal const int BST_FOCUS = 0x0008;

		internal const int PBM_SETBKCOLOR = 0x2001;
		internal const int EM_SETBKGNDCOLOR = 0x443;

		internal const int STM_SETIMAGE = 0x172;

		internal const int CBN_ERRSPACE = -1;
		internal const int CBN_SELCHANGE = 1;
		internal const int CBN_DBLCLK = 2;
		internal const int CBN_SETFOCUS = 3;
		internal const int CBN_KILLFOCUS = 4;
		internal const int CBN_EDITCHANGE = 5;
		internal const int CBN_EDITUPDATE = 6;
		internal const int CBN_DROPDOWN = 7;
		internal const int CBN_CLOSEUP = 8;
		internal const int CBN_SELENDOK = 9;
		internal const int CBN_SELENDCANCEL = 10;

		internal const int CB_ERR = -1;
		internal const int CB_ERRSPACE = -2;
		internal const int CB_GETEDITSEL = 0x0140;
		internal const int CB_LIMITTEXT = 0x0141;
		internal const int CB_SETEDITSEL = 0x0142;
		internal const int CB_ADDSTRING = 0x0143;
		internal const int CB_DELETESTRING = 0x0144;
		internal const int CB_DIR = 0x0145;
		internal const int CB_GETCOUNT = 0x0146;
		internal const int CB_GETCURSEL = 0x0147;
		internal const int CB_GETLBTEXT = 0x0148;
		internal const int CB_GETLBTEXTLEN = 0x0149;
		internal const int CB_INSERTSTRING = 0x014A;
		internal const int CB_RESETCONTENT = 0x014B;
		internal const int CB_FINDSTRING = 0x014C;
		internal const int CB_SELECTSTRING = 0x014D;
		internal const int CB_SETCURSEL = 0x014E;
		internal const int CB_SHOWDROPDOWN = 0x014F;
		internal const int CB_GETITEMDATA = 0x0150;
		internal const int CB_SETITEMDATA = 0x0151;
		internal const int CB_GETDROPPEDCONTROLRECT = 0x0152;
		internal const int CB_SETITEMHEIGHT = 0x0153;
		internal const int CB_GETITEMHEIGHT = 0x0154;
		internal const int CB_SETEXTENDEDUI = 0x0155;
		internal const int CB_GETEXTENDEDUI = 0x0156;
		internal const int CB_GETDROPPEDSTATE = 0x0157;
		internal const int CB_FINDSTRINGEXACT = 0x0158;
		internal const int CB_SETLOCALE = 0x0159;
		internal const int CB_GETLOCALE = 0x015A;
		internal const int CB_GETTOPINDEX = 0x015b;
		internal const int CB_SETTOPINDEX = 0x015c;
		internal const int CB_GETHORIZONTALEXTENT = 0x015d;
		internal const int CB_SETHORIZONTALEXTENT = 0x015e;
		internal const int CB_GETDROPPEDWIDTH = 0x015f;
		internal const int CB_SETDROPPEDWIDTH = 0x0160;
		internal const int CB_INITSTORAGE = 0x0161;

		internal const int LB_OKAY = 0;
		internal const int LB_ERR = -1;
		internal const int LB_ERRSPACE = -2;

		internal const int LB_ADDSTRING = 0x0180;
		internal const int LB_INSERTSTRING = 0x0181;
		internal const int LB_DELETESTRING = 0x0182;
		internal const int LB_SELITEMRANGEEX = 0x0183;
		internal const int LB_RESETCONTENT = 0x0184;
		internal const int LB_SETSEL = 0x0185;
		internal const int LB_SETCURSEL = 0x0186;
		internal const int LB_GETSEL = 0x0187;
		internal const int LB_GETCURSEL = 0x0188;
		internal const int LB_GETTEXT = 0x0189;
		internal const int LB_GETTEXTLEN = 0x018A;
		internal const int LB_GETCOUNT = 0x018B;
		internal const int LB_SELECTSTRING = 0x018C;
		internal const int LB_DIR = 0x018D;
		internal const int LB_GETTOPINDEX = 0x018E;
		internal const int LB_FINDSTRING = 0x018F;
		internal const int LB_GETSELCOUNT = 0x0190;
		internal const int LB_GETSELITEMS = 0x0191;
		internal const int LB_SETTABSTOPS = 0x0192;
		internal const int LB_GETHORIZONTALEXTENT = 0x0193;
		internal const int LB_SETHORIZONTALEXTENT = 0x0194;
		internal const int LB_SETCOLUMNWIDTH = 0x0195;
		internal const int LB_ADDFILE = 0x0196;
		internal const int LB_SETTOPINDEX = 0x0197;
		internal const int LB_GETITEMRECT = 0x0198;
		internal const int LB_GETITEMDATA = 0x0199;
		internal const int LB_SETITEMDATA = 0x019A;
		internal const int LB_SELITEMRANGE = 0x019B;
		internal const int LB_SETANCHORINDEX = 0x019C;
		internal const int LB_GETANCHORINDEX = 0x019D;
		internal const int LB_SETCARETINDEX = 0x019E;
		internal const int LB_GETCARETINDEX = 0x019F;
		internal const int LB_SETITEMHEIGHT = 0x01A0;
		internal const int LB_GETITEMHEIGHT = 0x01A1;
		internal const int LB_FINDSTRINGEXACT = 0x01A2;
		internal const int LB_SETLOCALE = 0x01A5;
		internal const int LB_GETLOCALE = 0x01A6;
		internal const int LB_SETCOUNT = 0x01A7;
		internal const int LB_INITSTORAGE = 0x01A8;
		internal const int LB_ITEMFROMPOINT = 0x01A9;

		internal const int LBN_ERRSPACE = -2;
		internal const int LBN_SELCHANGE = 1;
		internal const int LBN_DBLCLK = 2;
		internal const int LBN_SELCANCEL = 3;
		internal const int LBN_SETFOCUS = 4;
		internal const int LBN_KILLFOCUS = 5;

		internal const int LBS_NOTIFY = 0x0001;
		internal const int LBS_SORT = 0x0002;
		internal const int LBS_NOREDRAW = 0x0004;
		internal const int LBS_MULTIPLESEL = 0x0008;
		internal const int LBS_OWNERDRAWFIXED = 0x0010;
		internal const int LBS_OWNERDRAWVARIABLE = 0x0020;
		internal const int LBS_HASSTRINGS = 0x0040;
		internal const int LBS_USETABSTOPS = 0x0080;
		internal const int LBS_NOINTEGRALHEIGHT = 0x0100;
		internal const int LBS_MULTICOLUMN = 0x0200;
		internal const int LBS_WANTKEYBOARDINPUT = 0x0400;
		internal const int LBS_EXTENDEDSEL = 0x0800;
		internal const int LBS_DISABLENOSCROLL = 0x1000;
		internal const int LBS_NODATA = 0x2000;
		internal const int LBS_NOSEL = 0x4000;
		internal const int LBS_COMBOBOX = 0x8000;
		internal const int LBS_STANDARD = LBS_NOTIFY | LBS_SORT | WS_VSCROLL | WS_BORDER;

		internal const int LVM_FIRST = 0x1000;//ListView messages
		internal const int TV_FIRST = 0x1100;//TreeView messages
		internal const int LVM_GETITEMCOUNT = LVM_FIRST + 4;
		internal const int LVM_GETHEADER = LVM_FIRST + 31;
		internal const int LVM_GETNEXTITEM = LVM_FIRST + 12;
		internal const int LVM_GETITEMTEXT = LVM_FIRST + 115;

		internal const int LVNI_FOCUSED = 0x0001;
		internal const int LVNI_SELECTED = 0x0002;
		internal const int LVM_GETSELECTEDCOUNT = LVM_FIRST + 50;

		internal const int HDM_FIRST = 0x1200;//Header messages
		internal const int TCM_FIRST = 0x1300;//Tab control messages
		internal const int TVM_SORTCHILDREN = TV_FIRST + 19;
		internal const int TCM_SETCURFOCUS = (TCM_FIRST + 48);
		internal const int TCM_GETCURSEL = (TCM_FIRST + 11);

		internal const int HDM_GETITEMCOUNT = HDM_FIRST;

		internal const int TCS_SCROLLOPPOSITE = 0x0001;// assumes multiline tab
		internal const int TCS_BOTTOM = 0x0002;
		internal const int TCS_RIGHT = 0x0002;
		internal const int TCS_MULTISELECT = 0x0004;// allow multi-select in button mode
		internal const int TCS_FLATBUTTONS = 0x0008;
		internal const int TCS_FORCEICONLEFT = 0x0010;
		internal const int TCS_FORCELABELLEFT = 0x0020;
		internal const int TCS_HOTTRACK = 0x0040;
		internal const int TCS_VERTICAL = 0x0080;
		internal const int TCS_TABS = 0x0000;
		internal const int TCS_BUTTONS = 0x0100;
		internal const int TCS_SINGLELINE = 0x0000;
		internal const int TCS_MULTILINE = 0x0200;
		internal const int TCS_RIGHTJUSTIFY = 0x0000;
		internal const int TCS_FIXEDWIDTH = 0x0400;
		internal const int TCS_RAGGEDRIGHT = 0x0800;
		internal const int TCS_FOCUSONBUTTONDOWN = 0x1000;
		internal const int TCS_OWNERDRAWFIXED = 0x2000;
		internal const int TCS_TOOLTIPS = 0x4000;
		internal const int TCS_FOCUSNEVER = 0x8000;

		internal const uint LVM_GETCOLUMN = LVM_FIRST + 95;
		internal const uint LVM_SETCOLUMN = LVM_FIRST + 96;
		internal const uint LVCF_FMT = 0x1;
		internal const uint LVCF_IMAGE = 0x10;
		internal const int LVCFMT_IMAGE = 0x800;
		internal const int LVCFMT_BITMAP_ON_RIGHT = 0x1000;

		internal const int NM_FIRST = 0;
		internal const int NM_CLICK = NM_FIRST - 2;
		internal const int WM_REFLECT = 0x2000;
		internal const int WM_NOTIFY = 0x004e;

		internal const int WINDOW_TEXT_SIZE = 32767;

		internal const int ALTERNATE = 1;
		internal const int WINDING = 2;

		internal const int UNCHECKED = 1048576;
		internal const int CHECKED = 1048592;
		internal const int UNCHECKED_FOCUSED = 1048580; // if control is focused
		internal const int CHECKED_FOCUSED = 1048596; // if control is focused

		// GlobalAlloc flag SetClipboardData requires of the memory it takes ownership of.
		internal const uint GMEM_MOVEABLE = 0x0002;

		internal const int CF_TEXT = 1;
		internal const int CF_BITMAP = 2;
		internal const int CF_METAFILEPICT = 3;
		internal const int CF_SYLK = 4;
		internal const int CF_DIF = 5;
		internal const int CF_TIFF = 6;
		internal const int CF_OEMTEXT = 7;
		internal const int CF_DIB = 8;
		internal const int CF_PALETTE = 9;
		internal const int CF_PENDATA = 10;
		internal const int CF_RIFF = 11;
		internal const int CF_WAVE = 12;
		internal const int CF_UNICODETEXT = 13;
		internal const int CF_ENHMETAFILE = 14;
		internal const int CF_HDROP = 15;
		internal const int CF_LOCALE = 16;
		internal const int CF_DIBV5 = 17;
		internal const int CF_OWNERDISPLAY = 0x0080;
		internal const int CF_DSPTEXT = 0x0081;
		internal const int CF_DSPBITMAP = 0x0082;
		internal const int CF_DSPMETAFILEPICT = 0x0083;
		internal const int CF_DSPENHMETAFILE = 0x008E;
		internal const int CF_PRIVATEFIRST = 0x0200;
		internal const int CF_PRIVATELAST = 0x02FF;
		internal const int CF_GDIOBJFIRST = 0x0300;
		internal const int CF_GDIOBJLAST = 0x03FF;

		internal const int HC_ACTION = 0;
		internal const int HC_GETNEXT = 1;
		internal const int HC_SKIP = 2;
		internal const int HC_NOREMOVE = 3;
		internal const int HC_NOREM = HC_NOREMOVE;
		internal const int HC_SYSMODALON = 4;
		internal const int HC_SYSMODALOFF = 5;

		//WM_KEYUP/DOWN/CHAR HIWORD(lParam) flags
		public const int KF_ALTDOWN = 0x2000;

		public const int KF_DLGMODE = 0x0800;
		public const int KF_EXTENDED = 0x0100;
		public const int KF_MENUMODE = 0x1000;
		public const int KF_REPEAT = 0x4000;
		public const int KF_UP = 0x8000;
		public const int LLKHF_ALTDOWN = (KF_ALTDOWN >> 8);

		//Low level hook flags
		public const uint LLKHF_EXTENDED = (KF_EXTENDED >> 8);

		public const uint LLKHF_INJECTED = 0x00000010;
		public const uint LLKHF_LOWER_IL_INJECTED = 0x00000002;
		public const uint LLKHF_UP = (KF_UP >> 8);//0x00000020
		public const uint LLMHF_INJECTED = 0x00000001;//0x00000080
		public const uint LLMHF_LOWER_IL_INJECTED = 0x00000002;

		public const int STILL_ACTIVE = 0x00000103;
		public const int THREAD_BASE_PRIORITY_LOWRT = 15;  // value that gets a thread to LowRealtime-1
		public const int THREAD_BASE_PRIORITY_MAX = 2;   // maximum thread base priority boost
		public const int THREAD_BASE_PRIORITY_MIN = (-2);  // minimum thread base priority boost
		public const int THREAD_BASE_PRIORITY_IDLE = (-15); // value that gets a thread to idle

		public const int THREAD_PRIORITY_LOWEST = THREAD_BASE_PRIORITY_MIN;
		public const int THREAD_PRIORITY_BELOW_NORMAL = (THREAD_PRIORITY_LOWEST + 1);
		public const int THREAD_PRIORITY_NORMAL = 0;
		public const int THREAD_PRIORITY_HIGHEST = THREAD_BASE_PRIORITY_MAX;
		public const int THREAD_PRIORITY_ABOVE_NORMAL = (THREAD_PRIORITY_HIGHEST - 1);
		public const int THREAD_PRIORITY_ERROR_RETURN = (0x7fffffff);
		public const int THREAD_PRIORITY_TIME_CRITICAL = THREAD_BASE_PRIORITY_LOWRT;
		public const int THREAD_PRIORITY_IDLE = THREAD_BASE_PRIORITY_IDLE;
		public const int THREAD_MODE_BACKGROUND_BEGIN = 0x00010000;
		public const int THREAD_MODE_BACKGROUND_END = 0x00020000;
		public const int CT_CTYPE1 = 0x00000001;  // ctype 1 information
		public const int CT_CTYPE2 = 0x00000002;  // ctype 2 information
		public const int CT_CTYPE3 = 0x00000004;  // ctype 3 information
		public const int C3_NONSPACING = 0x0001;// nonspacing character
		public const int C3_DIACRITIC = 0x0002;// diacritic mark
		public const int C3_VOWELMARK = 0x0004;// vowel mark
		public const int C3_SYMBOL = 0x0008;// symbols
		public const int C3_KATAKANA = 0x0010;// katakana character
		public const int C3_HIRAGANA = 0x0020;// hiragana character
		public const int C3_HALFWIDTH = 0x0040;// half width character
		public const int C3_FULLWIDTH = 0x0080;// full width character
		public const int C3_IDEOGRAPH = 0x0100;// ideographic character
		public const int C3_KASHIDA = 0x0200;// Arabic kashida character
		public const int C3_LEXICAL = 0x0400;// lexical character
		public const int C3_HIGHSURROGATE = 0x0800;// high surrogate code unit
		public const int C3_LOWSURROGATE = 0x1000;// low surrogate code unit
		public const int C3_ALPHA = 0x8000;// any linguistic char (C1_ALPHA)
		public const int C3_NOTAPPLICABLE = 0x0000;// ctype 3 is not applicable

		public const int WH_MIN = -1;
		public const int WH_MSGFILTER = -1;
		public const int WH_JOURNALRECORD = 0;
		public const int WH_JOURNALPLAYBACK = 1;
		public const int WH_KEYBOARD = 2;
		public const int WH_GETMESSAGE = 3;
		public const int WH_CALLWNDPROC = 4;
		public const int WH_CBT = 5;
		public const int WH_SYSMSGFILTER = 6;
		public const int WH_MOUSE = 7;

		public const int WH_HARDWARE = 8;
		public const int WH_DEBUG = 9;
		public const int WH_SHELL = 10;
		public const int WH_FOREGROUNDIDLE = 11;
		public const int WH_CALLWNDPROCRET = 12;
		public const int WH_KEYBOARD_LL = 13;
		public const int WH_MOUSE_LL = 14;
		public const int WH_MAX = 14;
		public const int WH_MINHOOK = WH_MIN;
		public const int WH_MAXHOOK = WH_MAX;
		public const int JOYERR_BASE = 160;
		public const int JOYERR_NOERROR = 0;
		public const int JOYERR_PARMS = JOYERR_BASE + 5;
		public const int JOYERR_NOCANDO = JOYERR_BASE + 6;
		public const int JOYERR_UNPLUGGED = JOYERR_BASE + 7;

		public const int JOYCAPS_HASZ = 0x0001;
		public const int JOYCAPS_HASR = 0x0002;
		public const int JOYCAPS_HASU = 0x0004;
		public const int JOYCAPS_HASV = 0x0008;
		public const int JOYCAPS_HASPOV = 0x0010;
		public const int JOYCAPS_POV4DIR = 0x0020;
		public const int JOYCAPS_POVCTS = 0x0040;

		public const int JOY_POVCENTERED = -1;
		public const int JOY_POVFORWARD = 0;
		public const int JOY_POVRIGHT = 9000;
		public const int JOY_POVBACKWARD = 18000;
		public const int JOY_POVLEFT = 27000;

		public const int JOY_RETURNX = 0x00000001;
		public const int JOY_RETURNY = 0x00000002;
		public const int JOY_RETURNZ = 0x00000004;
		public const int JOY_RETURNR = 0x00000008;
		public const int JOY_RETURNU = 0x00000010;     /* axis 5 */
		public const int JOY_RETURNV = 0x00000020;     /* axis 6 */
		public const int JOY_RETURNPOV = 0x00000040;
		public const int JOY_RETURNBUTTONS = 0x00000080;
		public const int JOY_RETURNRAWDATA = 0x00000100;
		public const int JOY_RETURNPOVCTS = 0x00000200;
		public const int JOY_RETURNCENTERED = 0x00000400;
		public const int JOY_USEDEADZONE = 0x00000800;

		public const int JOY_RETURNALL = (JOY_RETURNX | JOY_RETURNY | JOY_RETURNZ |
										  JOY_RETURNR | JOY_RETURNU | JOY_RETURNV |
										  JOY_RETURNPOV | JOY_RETURNBUTTONS);

		public const long MB_SETFOREGROUND = 0x00010000L;

		public const uint MSG_OFFSET_MOUSE_MOVE = 0x80000000;
		public const uint GET_MODULE_HANDLE_EX_FLAG_PIN = 1;

		internal const long ERROR_ALREADY_EXISTS = 183L;
		internal const long ERROR_INVALID_HOOK_HANDLE = 1404L;

		public const int IMAGE_ICON = 1;
		public const int LR_DEFAULTCOLOR = 0x0000_0000;
		public const int LR_LOADFROMFILE = 0x0000_0010;
		public const int LR_CREATEDIBSECTION = 0x0000_2000;

		internal const string dwmapi = "dwmapi.dll",
							  kernel32 = "kernel32.dll",
							  shell32 = "shell32.dll",
							  user32 = "user32.dll",
							  gdi32 = "gdi32.dll",
							  version = "version.dll",
							  winmm = "winmm.dll",
							  advapi = "advapi32.dll",
							  ole32 = "ole32.dll",
							  oleacc = "oleacc.dll",
							  oleaut = "oleaut32.dll",
							  psapi = "psapi.dll",
							  combase = "combase.dll",
							  gdiplus = "gdiplus.dll";

		internal const uint CLR_INVALID = 0xFFFFFFFF;

		internal const uint RDW_INVALIDATE = 0x0001;
		internal const uint RDW_ERASE = 0x0004;
		internal const uint RDW_ALLCHILDREN = 0x0080;
		internal const uint RDW_UPDATENOW = 0x0100;

	}
}
