namespace Keysharp.Scripting
{
	public static class Keywords
	{
		public const bool IsDebug =
#if DEBUG
			true
#else
			false
#endif
			;

#if WINDOWS
		public const string LibraryExtension = ".dll";
#else
		public const string LibraryExtension = ".so";
#endif

		public static readonly char[] BothBraces = "{}".ToCharArray();
		public static readonly char[] BothParens = "()".ToCharArray();
		public static readonly char[] BothBrackets = "[]".ToCharArray();

		public static readonly char[] Quote = "\"".ToCharArray();
		public static readonly char[] Comma = ",".ToCharArray();
		public static readonly char[] DashSlash = "-/".ToCharArray();
		public static readonly char[] FatArrow = "=>".ToCharArray();
		//internal const string FloorDivide = "//";
		internal const char Add = '+';
		internal const char Address = '&';
		internal const string AndTxt = "and";
		internal const char ArrayClose = ']';
		internal const char ArrayOpen = '[';
		internal const string AssignEqual = "=";
		internal const char AssignPre = ':';
		internal const string BetweenTxt = "between";
		internal const char BitAND = '&';
		//internal const string Or = "||";
		internal const char BitNOT = '~';
		internal const char BitOR = '|';
		//internal const string And = "&&";
		internal const char BitXOR = '^';
		internal const char BlockClose = '}';
		internal const char BlockOpen = '{';
		internal const string BooleanAnd = "&&";
		internal const string BooleanOr = "||";
		internal const string Comments0 = "comments";
		internal const string Comments1 = "comment";
		internal const string Comments2 = "com";
		internal const string Comments3 = "c";
		internal const char Concatenate = '.';
		internal const string ContainsTxt = "contains";
		internal const char CR = '\r';
		internal const char DefaultComment = ';';
		internal const char DefaultEscape = '`';
		internal const char DefaultMulticast = ',';
		internal const char DefaultResolve = '%';
		internal const char Dereference = '*';
		internal const char Directive = '#';
		internal const string DirvCommentFlag = "commentflag";
		internal const string DirvEscapeChar = "escapechar";
		internal const string DirvInclude = "include";
		internal const string DirvIncludeAgain = "includeagain";
		internal const char Divide = '/';

        internal const string ClassStaticPrefix = "static";
		internal const string InternalPrefix = "_ks_";
        internal const string TempVariablePrefix = InternalPrefix + "temp";
        internal const string AnonymousLambdaPrefix = InternalPrefix + "AnonLambda_";
        internal const string AnonymousFatArrowLambdaPrefix = InternalPrefix + "AnonFatArrowLambda_";

		public const string MainNamespaceName = "Keysharp.CompiledMain";
		public const string MainClassName = "Program";
		public const string UserDeclaredClassesContainerName = "UserDeclaredClasses";
		public const string AutoExecSectionName = "AutoExecSection";
		public const string MainScriptVariableName = "MainScript";

		public static Dictionary<string, string> TypeNameAliases = new(StringComparer.OrdinalIgnoreCase)
		{
			{ "KeysharpObject", "Object" },
			{ "FuncObj", "Func" }
		};

		internal static FrozenSet<string> ClassReservedKeywords = FrozenSet.Create(StringComparer.OrdinalIgnoreCase,
				"Call", "__New", "__Init", "__Get", "__Set", "__Item", "__Class", "__StaticInit");

        //internal const string BitShiftLeft = "<<";
        //internal const string BitShiftRight = ">>";
        //internal const string GreaterOrEqual = ">=";
        //internal const string LessOrEqual = "<=";
        internal const char Equal = '=';

		internal const string ErrorLevelStr = "ErrorLevel";
		internal const string ExCommand = "Invalid command name";
		internal const string ExContJoinTooLong = "Join string for continuation section is too long";
		internal const string ExEmptySource = "No code to parse";
		internal const string ExEmptyVarRef = "Empty variable reference";
		internal const string ExEnd = "Unexpected end of file";
		internal const string ExFileNotFound = "File or directory not found";
		internal const string ExFlowArgNotReq = "Argument not expected";
		internal const string ExFlowArgReq = "Argument is required";
		internal const string ExGeneric = "Unexpected exception";
		internal const string ExIllegalCommentFlag = "Illegal comment flag";
		internal const string ExIncludeNotFound = "Include file not found";
		internal const string ExIntlLineMismatch = "Line and index counts mismatched";
		internal const string ExInvalidExponent = "Invalid exponent.";
		internal const string ExInvalidExpression = "Invalid expression term";
		internal const string ExInvalidVarName = "Invalid variable name";
		internal const string ExInvalidVarToken = "Invalid character in variable name";
		internal const string ExMultiStr = "Unrecognized multiline string option";
		internal const string ExNoDynamicVars = "Dynamic variables are not permitted";
		internal const string ExSymbolMismatch = "Parser returned incorrect token";
		internal const string ExTooFewParams = "Too few parameters passed to function";
		internal const string ExUnbalancedBraces = "Unbalanced braces in expression";
		internal const string ExUnbalancedBrackets = "Unbalanced brackets in expression";
		internal const string ExUnbalancedParens = "Unbalanced parentheses in expression";
		internal const string ExUnclosedBlock = "Unclosed block";
		internal const string ExUnexpected = "Unexpected symbol.";
		internal const string ExUnknownDirv = "Unrecognized directive";
		internal const string ExUntermStr = "Unterminated string";
		internal const string FalseTxt = "false";
		internal const string FlowBreak = "break";
		internal const string FlowCase = "case";
		internal const string FlowCatch = "catch";
		internal const string FlowClass = "class";
		internal const string FlowContinue = "continue";
		internal const string FlowDefault = "default";
		internal const string FlowElse = "else";
		internal const string FlowExtends = "extends";
		internal const string FlowFinally = "finally";
		internal const string FlowFor = "for";
		internal const string FlowGet = "get";
		internal const string FlowGosub = "gosub";
		internal const string FlowGoto = "goto";
		internal const string FlowIf = "if";
		internal const string FlowLoop = "loop";
		internal const string FlowReturn = "return";
		internal const string FlowSet = "set";
		internal const string FlowSwitch = "switch";
		internal const string FlowThrow = "throw";
		internal const string FlowTry = "try";
		internal const string FlowUntil = "until";
		internal const string FlowWhile = "while";
		internal const string FunctionGlobal = "global";
		internal const string FunctionLocal = "local";
		internal const string FunctionParamRef = "byref";
		internal const string FunctionStatic = "static";
		internal const char Greater = '>';
		internal const char HotkeyBound = ':';
		internal const string HotkeySignal = "::";
		internal const string InTxt = "in";
		internal const string invokeCommand = "IsCommand";
		internal const string IsTxt = "is";
		internal const string Join = "join";
		internal const string Keyword_A = "a";
		internal const string Keyword_Abort = "abort";
		internal const string Keyword_AboveNormal = "abovenormal";
		internal const string Keyword_Abs = "abs";
		internal const string Keyword_Acos = "acos";
		internal const string Keyword_ActiveX = "activex";
		internal const string Keyword_Add = "add";
		internal const string Keyword_ahk = "ahk_";
		internal const string Keyword_ahk_class = "ahk_class";
		internal const string Keyword_ahk_exe = "ahk_exe";
		internal const string Keyword_ahk_group = "ahk_group";
		internal const string Keyword_ahk_id = "ahk_id";
		internal const string Keyword_ahk_pid = "ahk_pid";
		internal const string Keyword_All = "all";
		internal const string Keyword_Alnum = "alnum";
		internal const string Keyword_Alpha = "alpha";
		internal const string Keyword_AltSubmit = "altsubmit";
		internal const string Keyword_AltTab = "alttab";
		internal const string Keyword_AltTabAndMenu = "alttabandmenu";
		internal const string Keyword_AltTabmenu = "alttabmenu";
		internal const string Keyword_AltTabMenuDismiss = "alttabmenudismiss";
		internal const string Keyword_AlwaysOnTop = "alwaysontop";
		internal const string Keyword_And = "and";
		internal const string Keyword_ARGB = "argb";
		internal const string Keyword_Asc = "asc";
		internal const string Keyword_Asin = "asin";
		internal const string Keyword_Atan = "atan";
		internal const string Keyword_Attachments = "attachments";
		internal const string Keyword_Auto = "auto";
		internal const string Keyword_AutoHdr = "autohdr";
		internal const string Keyword_AutoSize = "autosize";
		internal const string Keyword_Background = "background";
		internal const string Keyword_BackGroundImage = "backgroundimage";
		internal const string Keyword_BackgroundTrans = "backgroundtrans";
		internal const string Keyword_BackSpaceS = "B";
		internal const string Keyword_Bcc = "bcc";
		internal const string Keyword_BelowNormal = "belownormal";
		internal const string Keyword_Between = "between";
		internal const string Keyword_BitAnd = "bitand";
		internal const string Keyword_BitNot = "bitnot";
		internal const string Keyword_BitOr = "bitor";
		internal const string Keyword_BitShiftLeft = "bitshiftleft";
		internal const string Keyword_BitShiftRight = "bitshiftright";
		internal const string Keyword_BitXor = "bitxor";
		internal const string Keyword_Bold = "bold";
		internal const string Keyword_Border = "border";
		internal const string Keyword_Borderless = "B";
		internal const string Keyword_Bottom = "bottom";
		internal const string Keyword_Button = "button";
		internal const string Keyword_Buttons = "buttons";
		internal const string Keyword_ByRef = "byref";
		internal const string Keyword_Cancel = "cancel";
		internal const string Keyword_Capacity = "capacity";
		internal const string Keyword_Caption = "caption";
		internal const string Keyword_Caret = "caret";
		internal const string Keyword_Case = "case";
		internal const string Keyword_CaseLocale = "caselocale";
		internal const string Keyword_CC = "cc";
		internal const string Keyword_CDROM = "CDROM";
		internal const string Keyword_Ceil = "ceil";
		internal const string Keyword_Center = "center";
		internal const string Keyword_Centered = "C";
		internal const string Keyword_Check = "check";
		internal const string Keyword_Check3 = "check3";
		internal const string Keyword_CheckBox = "checkbox";
		internal const string Keyword_Checked = "checked";
		internal const string Keyword_CheckedGray = "checkedgray";
		internal const string Keyword_Choose = "choose";
		internal const string Keyword_ChooseString = "choosestring";
		internal const string Keyword_Chr = "chr";
		internal const string Keyword_Click = "click";
		internal const string Keyword_Client = "client";
		internal const string Keyword_Close = "close";
		internal const string Keyword_Color = "color";
		internal const string Keyword_Column = "column";
		internal const string Keyword_ComboBox = "combobox";
		internal const string Keyword_Contains = "contains";
		internal const string Keyword_ControlList = "controllist";
		internal const string Keyword_Cos = "cos";
		internal const string Keyword_Count = "count";
		internal const string Keyword_CSV = "csv";
		internal const string Keyword_Cursor = "cursor";
		internal const string Keyword_Custom = "custom";
		internal const string Keyword_Date = "date";
		internal const string Keyword_DateTime = "datetime";
		internal const string Keyword_Days = "days";
		internal const string Keyword_DDL = "ddl";
		internal const string Keyword_Default = "default";
		internal const string Keyword_Delete = "delete";
		internal const string Keyword_DeleteAll = "deleteall";
		internal const string Keyword_Delimiter = "delimiter";
		internal const string Keyword_Deref = "deref";
		internal const string Keyword_Desc = "desc";
		internal const string Keyword_Destroy = "destroy";
		internal const string Keyword_Digit = "digit";
		internal const string Keyword_Disable = "disable";
		internal const string Keyword_Disabled = "disabled";
		internal const string Keyword_Down = "down";
		internal const string Keyword_DownS = "D";
		internal const string Keyword_DropDownList = "dropdownlist";
		internal const string Keyword_Edit = "edit";
		internal const string Keyword_Eject = "eject";
		internal const string Keyword_Enable = "enable";
		internal const string Keyword_Enabled = "enabled";
		internal const string Keyword_Error = "error";
		internal const string Keyword_Exist = "exist";
		internal const string Keyword_Exit = "Exit";
		internal const string Keyword_ExitApp = "ExitApp";
		internal const string Keyword_ExitThread = "ExitThread";
		internal const string Keyword_Exp = "exp";
		internal const string Keyword_Expand = "expand";
		internal const string Keyword_Expanded = "expanded";
		internal const string Keyword_ExStyle = "exstyle";
		internal const string Keyword_Fast = "fast";
		internal const string Keyword_FileSystem = "filesystem";
		internal const string Keyword_FindAnyWhereS = "*";
		internal const string Keyword_First = "first";
		internal const string Keyword_FIXED = "Fixed";
		internal const string Keyword_Flash = "flash";
		internal const string Keyword_Float = "float";
		internal const string Keyword_Floor = "floor";
		internal const string Keyword_Focus = "focus";
		internal const string Keyword_Focused = "focused";
		internal const string Keyword_Font = "font";
		internal const string Keyword_FormatDecimal = "d";
		internal const string Keyword_FormatHex = "h";
		internal const string Keyword_From = "from";
		internal const string Keyword_Full = "full";
		internal const string Keyword_Global = "global";
		internal const string Keyword_Grid = "grid";
		internal const string Keyword_Group = "group";
		internal const string Keyword_GroupBox = "groupbox";
		internal const string Keyword_GuiButton = "Button";
		internal const string Keyword_GuiClose = "close";
		internal const string Keyword_GuiContextMenu = "contextmenu";
		internal const string Keyword_GuiDropFiles = "dropfiles";
		internal const string Keyword_GuiEscape = "escape";
		internal const string Keyword_GuiPrefix = "gui";
		internal const string Keyword_GuiSize = "size";
		internal const string Keyword_H = "H";
		internal const string Keyword_Hdr = "hdr";
		internal const string Keyword_Hex = "hex";
		internal const string Keyword_Hidden = "hidden";
		internal const string Keyword_Hide = "hide";
		internal const string Keyword_High = "high";
		internal const string Keyword_HKCC = "hkcc";
		internal const string Keyword_HKCR = "hkcr";
		internal const string Keyword_HKCU = "hkcu";
		internal const string Keyword_HKey_Classes_Root = "hkey_classes_root";
		internal const string Keyword_HKey_Current_Config = "hkey_current_config";
		internal const string Keyword_HKey_Current_User = "hkey_current_user";
		internal const string Keyword_HKey_Local_Machine = "hkey_local_machine";
		internal const string Keyword_HKey_Performance_Data = "hkey_performance_data";
		internal const string Keyword_HKey_Users = "hkey_users";
		internal const string Keyword_HKLM = "hklm";
		internal const string Keyword_HKPD = "hkpd";
		internal const string Keyword_HKU = "hku";
		internal const string Keyword_Horz = "horz";
		internal const string Keyword_Host = "host";
		internal const string Keyword_Hotkey = "hotkey";
		internal const char Keyword_HotkeyCombination = '&';
		internal const char Keyword_HotkeyIgnoreModifiers = '*';
		internal const char Keyword_HotkeyNoRecurse = '$';
		internal const char Keyword_HotkeyPassThrough = '~';
		internal const string Keyword_HotkeySC = "SC";
		internal const string Keyword_HotkeyVK = "VK";
		internal const char Keyword_HotstringAuto = '*';
		internal const char Keyword_HotstringBackspace = 'B';
		internal const char Keyword_HotstringCase = 'C';
		internal const char Keyword_HotstringNested = '?';
		internal const char Keyword_HotstringOff = '0';
		internal const char Keyword_HotstringOmitEnding = 'O';
		internal const char Keyword_HotstringReset = 'Z';
		internal const string Keyword_Hours = "hours";
		internal const string Keyword_HScroll = "hscroll";
		internal const string Keyword_Hwnd = "hwnd";
		internal const string Keyword_Icon = "Icon";
		internal const string Keyword_IconRight = "iconright";
		internal const string Keyword_IconSmall = "iconsmall";
		internal const string Keyword_ID = "id";
		internal const string Keyword_IDLast = "idlast";
		internal const string Keyword_IfWin = "__IFWIN";
		internal const string Keyword_IfWinActive = "ifwinactive";
		internal const string Keyword_IfWinExist = "ifwinexist";
		internal const string Keyword_IfWinNotActive = "ifwinnotactive";
		internal const string Keyword_IfWinNotExit = "ifwinnotexist";
		internal const string Keyword_Ignore = "ignore";
		internal const string Keyword_IgnoreS = "I";
		internal const string Keyword_ImageList = "imagelist";
		internal const string Keyword_In = "in";
		internal const char Keyword_IniKeyAssign = '=';
		internal const string Keyword_IniSectionOpen = "[";
		internal const string Keyword_Integer = "integer";
		internal const string Keyword_Interrupt = "interrupt";
		internal const string Keyword_Invert = "invert";
		internal const string Keyword_Is = "is";
		internal const string Keyword_Italic = "italic";
		internal const string Keyword_Join = "join";
		internal const string Keyword_Key = "KEY";
		internal const char Keyword_KeyNameClose = '}';
		internal const char Keyword_KeyNameOpen = '{';
		internal const string Keyword_Label = "label";
		internal const string Keyword_LastFound = "lastfound";
		internal const string Keyword_LastFoundExist = "lastfoundexist";
		internal const string Keyword_Left = "left";
		internal const string Keyword_Limit = "limit";
		internal const string Keyword_LimitS = "L";
		internal const string Keyword_Line = "line";
		internal const string Keyword_Linefeed = "\r\n";
		internal const string Keyword_Lines = "lines";
		internal const string Keyword_Link = "link";
		internal const string Keyword_List = "list";
		internal const string Keyword_ListBox = "listbox";
		internal const string Keyword_ListView = "listview";
		internal const string Keyword_Ln = "ln";
		internal const string Keyword_Local = "local";
		internal const string Keyword_Locale = "locale";
		internal const string Keyword_Lock = "lock";
		internal const string Keyword_Log = "log";
		internal const string Keyword_Logoff = "logoff";
		internal const string Keyword_LongDate = "longdate";
		internal const string Keyword_Low = "low";
		internal const string Keyword_Lower = "lower";
		internal const string Keyword_Lowercase = "lowercase";
		internal const string Keyword_LTrim = "ltrim";
		internal const string Keyword_MainWindow = "mainwindow";
		internal const string Keyword_Margin = "margin";
		internal const string Keyword_Max = "max";
		internal const string Keyword_Maximize = "maximize";
		internal const string Keyword_MaximizeBox = "maximizebox";
		internal const string Keyword_MaxSize = "maxsize";
		internal const string Keyword_Menu = "menu";
		internal const string Keyword_Min = "min";
		internal const string Keyword_Minimize = "minimize";
		internal const string Keyword_MinimizeBox = "minimizebox";
		internal const string Keyword_MinMax = "minmax";
		internal const string Keyword_MinSize = "minsize";
		internal const string Keyword_Minutes = "minutes";
		internal const string Keyword_Mod = "mod";
		internal const string Keyword_ModifiedS = "M";
		internal const char Keyword_ModifierAlt = '!';
		internal const string Keyword_ModifierAltGr = "<^>!";
		internal const char Keyword_ModifierCtrl = '^';
		internal const char Keyword_ModifierLeftPair = '<';
		internal const char Keyword_ModifierRightPair = '>';
		internal const char Keyword_ModifierShift = '+';
		internal const char Keyword_ModifierWin = '#';
		internal const string Keyword_MonthCal = "monthcal";
		internal const string Keyword_Months = "months";
		internal const string Keyword_Mouse = "mouse";
		internal const string Keyword_Move = "move";
		internal const string Keyword_MoveDraw = "movedraw";
		internal const string Keyword_Multi = "multi";
		internal const string Keyword_NA = "na";
		internal const string Keyword_NETWORK = "Network";
		internal const string Keyword_No = "no";
		internal const string Keyword_NoActivate = "noactivate";
		internal const string Keyword_NoDefault = "nodefault";
		internal const string Keyword_NoHide = "nohide";
		internal const string Keyword_NoIcon = "noicon";
		internal const string Keyword_NoMainWindow = "nomainwindow";
		internal const string Keyword_None = "none";
		internal const string Keyword_Norm = "norm";
		internal const string Keyword_Normal = "normal";
		internal const string Keyword_NoSort = "nosort";
		internal const string Keyword_NoSortHdr = "nosorthdr";
		internal const string Keyword_NoStandard = "nostandard";
		internal const string Keyword_Not = "not";
		internal const string Keyword_NoTab = "notab";
		internal const string Keyword_NotAlwaysOnTop = "A";
		internal const string Keyword_NoTicks = "noticks";
		internal const string Keyword_NoTimers = "notimers";
		internal const string Keyword_Number = "number";
		internal const string Keyword_Off = "off";
		internal const string Keyword_Ok = "ok";
		internal const string Keyword_On = "on";
		internal const string Keyword_Or = "or";
		internal const string Keyword_OwnDialogs = "owndialogs";
		internal const string Keyword_Owner = "owner";
		internal const string Keyword_Page = "page";
		internal const string Keyword_Parse = "parse";
		internal const string Keyword_Password = "password";
		internal const string Keyword_Pic = "pic";
		internal const string Keyword_Picture = "picture";
		internal const string Keyword_Pixel = "pixel";
		internal const char Keyword_Port = ':';
		internal const string Keyword_Pos = "pos";
		internal const string Keyword_Pow = "pow";
		internal const string Keyword_Priority = "priority";
		internal const string Keyword_ProcessName = "processname";
		internal const string Keyword_Progress = "progress";
		internal const string Keyword_ProgressRange = "R";
		internal const string Keyword_ProgressStartPos = "P";
		internal const string Keyword_R = "r";
		internal const string Keyword_Radio = "radio";
		internal const string Keyword_RAMDISK = "RAMDisk";
		internal const string Keyword_Random = "random";
		internal const string Keyword_Range = "range";
		internal const string Keyword_Raw = "raw";
		internal const string Keyword_Read = "read";
		internal const string Keyword_Readonly = "readonly";
		internal const string Keyword_Realtime = "realtime";
		internal const string Keyword_Redraw = "redraw";
		internal const string Keyword_Reg_Binary = "reg_binary";
		internal const string Keyword_Reg_Dword = "reg_dword";
		internal const string Keyword_Reg_Expand_Sz = "reg_expand_sz";
		internal const string Keyword_Reg_Multi_Sz = "reg_multi_sz";
		internal const string Keyword_Reg_Sz = "reg_sz";
		internal const string Keyword_RegEx = "regex";
		internal const string Keyword_Region = "region";
		internal const string Keyword_Relative = "relative";
		internal const string Keyword_REMOVABLE = "Removable";
		internal const string Keyword_Rename = "rename";
		internal const string Keyword_ReplyTo = "replyto";
		internal const string Keyword_Report = "report";
		internal const string Keyword_Resize = "resize";
		internal const string Keyword_Restore = "restore";
		internal const string Keyword_Retry = "retry";
		internal const string Keyword_Return = "Return";
		internal const string Keyword_RGB = "rgb";
		internal const string Keyword_Rich_Edit = "richedit";
		internal const string Keyword_Right = "right";
		internal const string Keyword_Round = "round";
		internal const string Keyword_RTrim = "rtrim";
		internal const string Keyword_Screen = "screen";
		internal const string Keyword_Seconds = "seconds";
		internal const string Keyword_Section = "section";
		internal const string Keyword_Select = "select";
		internal const string Keyword_Selected = "selected";
		internal const string Keyword_Serial = "serial";
		internal const string Keyword_SetLabel = "setlabel";
		internal const string Keyword_ShiftAltAab = "shiftalttab";
		internal const string Keyword_ShortDate = "shortdate";
		internal const string Keyword_Show = "show";
		internal const string Keyword_ShowInTaskbar = "T";
		internal const string Keyword_Simple = "simple";
		internal const string Keyword_Sin = "sin";
		internal const string Keyword_Single = "single";
		internal const string Keyword_Slider = "slider";
		internal const string Keyword_Slow = "slow";
		internal const string Keyword_Smooth = "smooth";
		internal const string Keyword_Sort = "sort";
		internal const string Keyword_SortDesc = "sortdesc";
		internal const string Keyword_Sqrt = "sqrt";
		internal const string Keyword_Standard = "standard";
		internal const string Keyword_Static = "static";
		internal const string Keyword_Status = "status";
		internal const string Keyword_StatusBar = "statusbar";
		internal const string Keyword_StatusCD = "statuscd";
		internal const string Keyword_Stretch = "stretch";
		internal const string Keyword_Strike = "strike";
		internal const string Keyword_Style = "style";
		internal const string Keyword_Submit = "submit";
		internal const string Keyword_SysMenu = "sysmenu";
		internal const string Keyword_Tab = "tab";
		internal const string Keyword_Tab2 = "tab2";
		internal const string Keyword_Tab3 = "tab3";
		internal const string Keyword_TabStop = "tabstop";
		internal const string Keyword_Tan = "tan";
		internal const string Keyword_TaskBar = "taskbar";
		internal const string Keyword_Text = "text";
		internal const string Keyword_Theme = "theme";
		internal const string Keyword_Thick = "thick";
		internal const string Keyword_TickInterval = "tickinterval";
		internal const string Keyword_Tile = "tile";
		internal const string Keyword_Time = "time";
		internal const string Keyword_TimeOutS = "T";
		internal const string Keyword_Tip = "tip";
		internal const string Keyword_Toggle = "toggle";
		internal const string Keyword_ToggleCheck = "togglecheck";
		internal const string Keyword_ToggleEnable = "toggleenable";
		internal const string Keyword_ToolTip = "tooltip";
		internal const string Keyword_ToolWindow = "toolwindow";
		internal const string Keyword_Top = "top";
		internal const string Keyword_Topmost = "topmost";
		internal const string Keyword_Trans = "Trans";
		internal const string Keyword_Transcolor = "transcolor";
		internal const string Keyword_Transparent = "transparent";
		internal const string Keyword_Tray = "tray";
		internal const string Keyword_TreeView = "treeview";
		internal const string Keyword_TryAgain = "tryagain";
		internal const string Keyword_Type = "type";
		internal const string Keyword_Uncheck = "uncheck";
		internal const string Keyword_Underline = "underline";
		internal const string Keyword_Uni = "uni";
		internal const string Keyword_Unicode = "unicode";
		internal const string Keyword_UNKNOWN = "Unknown";
		internal const string Keyword_Unlock = "unlock";
		internal const string Keyword_Up = "up";
		internal const string Keyword_UpDown = "updown";
		internal const string Keyword_Upper = "upper";
		internal const string Keyword_Uppercase = "uppercase";
		internal const string Keyword_UseErrorLevel = "useerrorlevel";
		internal const string Keyword_Variation = "variation";
		internal const string Keyword_Vertical = "vertical";
		internal const string Keyword_Vis = "vis";
		internal const string Keyword_VisFirst = "visfirst";
		internal const string Keyword_Visible = "visible";
		internal const string Keyword_VisibleS = "V";
		internal const string Keyword_VScroll = "vscroll";
		internal const string Keyword_W = "W";
		internal const string Keyword_Wait = "wait";
		internal const string Keyword_WaitClose = "waitclose";
		internal const string Keyword_WantCtrlA = "wantctrla";
		internal const string Keyword_WantF2 = "wantf2";
		internal const string Keyword_WantReturn = "wantreturn";
		internal const string Keyword_WantTab = "wanttab";
		internal const string Keyword_WDay = "wday";
		internal const string Keyword_WebBrowser = "webbrowser";
		internal const string Keyword_Window = "window";
		internal const string Keyword_Wrap = "wrap";
		internal const string Keyword_X = "X";
		internal const string Keyword_XDigit = "xdigit";
		internal const string Keyword_xm = "xm";
		internal const string Keyword_xp = "xp";
		internal const string Keyword_xs = "xs";
		internal const string Keyword_Y = "Y";
		internal const string Keyword_YDay = "yday";
		internal const string Keyword_YDay0 = "yday0";
		internal const string Keyword_YearMonth = "yearmonth";
		internal const string Keyword_Years = "years";
		internal const string Keyword_Yes = "yes";
		internal const string Keyword_ym = "ym";
		internal const string Keyword_yp = "yp";
		internal const string Keyword_ys = "ys";
		internal const string Keyword_YWeek = "yweek";
		internal const string Keyword_ZH = "ZH";
		internal const string Keyword_Zoom = "zoom";
		internal const string Keyword_ZW = "ZW";
		internal const string Keyword_ZX = "ZX";
		internal const string Keyword_ZY = "ZY";
		internal const char Less = '<';
		internal const char LF = '\n';
		internal const string LibDir = "lib";
		internal const string LibEnv = "AHKLIBPATH";
		internal const string LibExt = "ahk";
		internal const char LibSeperator = '_';
		internal const char Minus = '-';
		internal const string MsgBox = "msgbox";
		internal const char MultiComA = '/';
		internal const char MultiComB = '*';

		//internal const char Power = "**";
		internal const char Multiply = '*';
		internal const char Not = '!';
		internal const string NotTxt = "not";
		internal const string NullTxt = "null";
		internal const string Unset = "unset";
		//internal const string CaseSensitiveEqual = "==";
		//internal const string NotEqual = "!=";
		internal const string OrTxt = "or";
		internal const char ParenClose = ')';
		internal const char ParenOpen = '(';
		internal const string RawData = "raw";
		internal const char Reserved = '\0';
		internal const string RTrim = "rtrim";
		internal const char SingleSpace = ' ';
		internal const char StringBound = '"';
		internal const char StringBoundVerbatim = '\'';
		internal const char Subtract = '-';
		internal const char TernaryA = '?';
		internal const char TernaryB = ':';

		//internal const string AssignAdd = "+" + Equal;
		//internal const string AssignSubtract = "-" + Equal;
		//internal const string AssignMultiply = "*=" + Equal;
		//internal const string AssignDivide = "/" + Equal;
		//internal const string AssignFloorDivide = "//" + Equal;
		//internal const string AssignConcatenate = "." + Equal;
		//internal const string AssignBitOR = "|" + Equal;
		//internal const string AssignBitAND = "&" + Equal;
		//internal const string AssignBitXOR = "^" + Equal;
		//internal const string AssignShiftLeft = "<<" + Equal;
		//internal const string AssignShiftRight = ">>" + Equal;
		internal const string TrueTxt = "true";
		internal const string VarExt = "#_@";//$?";
		internal static readonly char[] BlockOpenAndSpace = [BlockOpen, SingleSpace];
		internal static readonly char[] BlockOpenParenOpen = [BlockOpen, ParenOpen];
		internal static readonly SearchValues<char> BlockOpenParenOpenSv = SearchValues.Create(BlockOpenParenOpen);
		internal static readonly char[] Parens = [ParenOpen, ParenClose];
		internal static readonly SearchValues<char> ParensSv = SearchValues.Create(Parens);
		internal static readonly char[] BlockOpenSpaceAndParens = BlockOpenAndSpace.Concat(Parens);
		internal static readonly char[] CrLf = "\r\n".ToCharArray();
		internal static readonly SearchValues<char> CrLfSv = SearchValues.Create(CrLf);
		internal static readonly object[] EmptyVariadicArgs = [System.Array.Empty<object>()];
		internal static readonly char[] SpaceColon = " :".ToCharArray();
		internal static readonly char[] Spaces = [CR, LF, SingleSpace, '\t', '\xA0', '\v', '\f'];
		internal static readonly SearchValues<char> SpacesSv = SearchValues.Create(Spaces);
		//public static readonly char[] BothBracesSpaces = BothBraces.Concat(Spaces);
		internal static readonly char[] SpacesQuotes = [CR, LF, SingleSpace, '\t', '\xA0', '"'];
		internal static readonly char[] SpaceTab = " \t".ToCharArray();
		internal static readonly SearchValues<char> SpaceTabSv = SearchValues.Create(SpaceTab);
		internal static readonly char[] SpaceTabComma = " \t,".ToCharArray();
		internal static readonly char[] SpaceTabOpenBrace = " \t{".ToCharArray();
		internal static readonly char[] SpaceTabOpenParen = " \t(".ToCharArray();
		internal static readonly SearchValues<char> SpaceTabOpenParenSv = SearchValues.Create(SpaceTabOpenParen);
		internal static readonly char[] TrimLine = "\t\r\n ".ToCharArray();
		internal static readonly char[] TrimSec = "[]".ToCharArray();
		//internal static readonly SearchValues<char> SpaceMultiDelimSv;
		internal static readonly char[] PlusMinus = ['+', '-'];

		internal static FrozenSet<string> keywords = FrozenSet.Create(
			AndTxt,
			OrTxt,
			NotTxt,
			TrueTxt,
			FalseTxt,
			NullTxt,
			IsTxt,
			FlowBreak,
			FlowContinue,
			FlowCase,
			FlowClass,
			FlowDefault,
			FlowFor,
			FlowElse,
			FlowExtends,
			FlowGosub,
			FlowGoto,
			FlowIf,
			FlowLoop,
			FlowReturn,
			FlowWhile,
			FunctionLocal,
			FunctionGlobal,
			FunctionStatic,
			FlowTry,
			FlowCatch,
			FlowFinally,
			FlowUntil,
			FlowSwitch,
			FlowThrow);

        public static string GetKeywords() => string.Join(' ', keywords);


#if !LEGACY
		internal const char LastVar = '$';
#endif
        internal static readonly char[] zerochars = ['0'];
#if !LEGACY
		internal const char Escape = DefaultEscape;
#else
		internal static readonly string Comment = DefaultComment.ToString();
		internal const char Escape = DefaultEscape;
#endif

#if !LEGACY
		internal static string Comment = DefaultComment.ToString();
#else
#endif

#if !LEGACY
		internal const char Resolve = DefaultResolve;
#else
		internal const char Multicast = DefaultMulticast;
		internal const char Resolve = DefaultResolve;
#endif

#if !LEGACY
		internal const char Multicast = DefaultMulticast;
#else
#endif
	}
}