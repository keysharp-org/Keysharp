#Include <assert>
#if OSX
#NoTrayIcon
#endif

#import KS { * }
; #Include %A_ScriptDir%/header.ahk

AssertEq(A_IsSuspended, 0, A_LineNumber)

Suspend 1

AssertEq(A_IsSuspended, 1, A_LineNumber)
		
Suspend false

AssertEq(A_IsCritical, 0, A_LineNumber)
		
Critical true
x := A_IsCritical

Assert(x > 0, A_LineNumber) 
	
Critical 0
x := A_IsCritical

AssertEq(x, 0, A_LineNumber)

AssertEq(A_TitleMatchMode, 2, A_LineNumber)

SetTitleMatchMode 1

AssertEq(A_TitleMatchMode, 1, A_LineNumber)

SetTitleMatchMode 2

AssertEq(A_TitleMatchMode, 2, A_LineNumber)
	
SetTitleMatchMode 3

AssertEq(A_TitleMatchMode, 3, A_LineNumber)
	
SetTitleMatchMode "RegEx"

AssertEq(A_TitleMatchMode, "regex", A_LineNumber)

SetTitleMatchMode "dummy"

AssertEq(A_TitleMatchMode, 2, A_LineNumber)

AssertEq(A_TitleMatchModeSpeed, "fast", A_LineNumber)

SetTitleMatchMode "fast"

AssertEq(A_TitleMatchModeSpeed, "fast", A_LineNumber)

SetTitleMatchMode "slow"

AssertEq(A_TitleMatchModeSpeed, "slow", A_LineNumber)

SetTitleMatchMode "dummy"

AssertEq(A_TitleMatchModeSpeed, "slow", A_LineNumber)

AssertEq(A_TitleMatchMode, 2, A_LineNumber)

SetTitleMatchMode 2 ; Reset it back for the function version of this test.
SetTitleMatchMode "fast"
	
Assert(!A_DetectHiddenWindows, A_LineNumber) 

DetectHiddenWindows 0

Assert(!A_DetectHiddenWindows, A_LineNumber)
	
DetectHiddenWindows 1

Assert(A_DetectHiddenWindows, A_LineNumber) 

DetectHiddenWindows "Off"

Assert(!A_DetectHiddenWindows, A_LineNumber)

DetectHiddenWindows 1

Assert(A_DetectHiddenWindows, A_LineNumber) 
				
DetectHiddenWindows "dummy"

Assert(A_DetectHiddenWindows, A_LineNumber) 

DetectHiddenWindows 0 ; Reset it back for the function version of this test.

Assert(!A_DetectHiddenWindows, A_LineNumber) 

DetectHiddenText 0

Assert(!A_DetectHiddenText, A_LineNumber) 
	
DetectHiddenText 1

Assert(A_DetectHiddenText, A_LineNumber) 

DetectHiddenText false

Assert(!A_DetectHiddenText, A_LineNumber) 

DetectHiddenText true

Assert(A_DetectHiddenText, A_LineNumber) 

DetectHiddenText 0 ; Reset it back for the function version of this test.

FileEncoding "utf-8"

AssertEq(A_FileEncoding, "utf-8", A_LineNumber)
	
FileEncoding "utf-8-raw"

AssertEq(A_FileEncoding, "utf-8-raw", A_LineNumber)

FileEncoding "unicode"

AssertEq(A_FileEncoding, "utf-16", A_LineNumber)

FileEncoding "utf-16"

AssertEq(A_FileEncoding, "utf-16", A_LineNumber)

FileEncoding "utf-16-raw"

AssertEq(A_FileEncoding, "utf-16-raw", A_LineNumber)

FileEncoding "ascii"

AssertEq(A_FileEncoding, "us-ascii", A_LineNumber)

; A name which cannot be resolved raises, as it does in AutoHotkey, and leaves the setting alone.
threw := 0

try
	FileEncoding "dummy"
catch ValueError
	threw := 1

Assert(threw && A_FileEncoding == "us-ascii", A_LineNumber)

AssertEq(A_SendLevel, 0, A_LineNumber)

SendLevel 0

AssertEq(A_SendLevel, 0, A_LineNumber)

SendLevel -1

AssertEq(A_SendLevel, 0, A_LineNumber)
	
SendLevel 1

AssertEq(A_SendLevel, 1, A_LineNumber)

SendLevel 100

AssertEq(A_SendLevel, 100, A_LineNumber)

SendLevel 101

AssertEq(A_SendLevel, 100, A_LineNumber)

SendLevel 0 ; Reset it back for the function version of this test.

AssertEq(A_StoreCapsLockMode, 1, A_LineNumber)

SetStoreCapsLockMode 0

AssertEq(A_StoreCapsLockMode, 0, A_LineNumber)

SetStoreCapsLockMode 1

AssertEq(A_StoreCapsLockMode, 1, A_LineNumber)

SetStoreCapsLockMode false

AssertEq(A_StoreCapsLockMode, 0, A_LineNumber)

SetStoreCapsLockMode 1

AssertEq(A_StoreCapsLockMode, 1, A_LineNumber)

SetStoreCapsLockMode "dummy"

AssertEq(A_StoreCapsLockMode, 1, A_LineNumber)

SetStoreCapsLockMode 1 ; Reset it back for the function version of this test.

AssertEq(A_KeyDelay, 10, A_LineNumber)

AssertEq(A_KeyDelayPlay, -1, A_LineNumber)

SetKeyDelay 10

AssertEq(A_KeyDelay, 10, A_LineNumber)

SetKeyDelay 20, 30

AssertEq(A_KeyDelay, 20, A_LineNumber)

SetKeyDelay , 40

AssertEq(A_KeyDelay, 20, A_LineNumber)

SetKeyDelay 50, 60, "Play"

AssertEq(A_KeyDelay, 20, A_LineNumber)

AssertEq(A_KeyDelayPlay, 50, A_LineNumber)

SetKeyDelay 10, -1 ; Reset it back for the function version of this test.
SetKeyDelay -1, -1, "Play"

AssertEq(A_WinDelay, 100, A_LineNumber)

SetWinDelay 200
	
AssertEq(A_WinDelay, 200, A_LineNumber)

SetWinDelay 100 ; Reset it back for the function version of this test.

AssertEq(A_ControlDelay, 20, A_LineNumber)

SetControlDelay 200

AssertEq(A_ControlDelay, 200, A_LineNumber)

SetControlDelay 20 ; Reset it back for the function version of this test.

AssertEq(A_MouseDelay, 10, A_LineNumber)

SetMouseDelay 200

AssertEq(A_MouseDelay, 200, A_LineNumber)

AssertEq(A_MouseDelayPlay, -1, A_LineNumber)
								
SetMouseDelay 300, "Play"

AssertEq(A_MouseDelay, 200, A_LineNumber)

AssertEq(A_MouseDelayPlay, 300, A_LineNumber)
		
SetMouseDelay 10 ; Reset it back for the function version of this test.
SetMouseDelay -1, "Play"

AssertEq(A_DefaultMouseSpeed, 2, A_LineNumber)

SetDefaultMouseSpeed 500

AssertEq(A_DefaultMouseSpeed, 500, A_LineNumber)
		
SetDefaultMouseSpeed 2 ; Reset it back for the function version of this test.

AssertEq(A_CoordModeToolTip, "Client", A_LineNumber)

AssertEq(A_CoordModePixel, "Client", A_LineNumber)
	
AssertEq(A_CoordModeMouse, "Client", A_LineNumber)
	
AssertEq(A_CoordModeCaret, "Client", A_LineNumber)
	
AssertEq(A_CoordModeMenu, "Client", A_LineNumber)

CoordMode "ToolTip", "Screen"

AssertEq(A_CoordModeToolTip, "Screen", A_LineNumber)

CoordMode "Pixel", "Client"

AssertEq(A_CoordModePixel, "Client", A_LineNumber)

CoordMode "Mouse", "Window"

Assert(A_CoordModeMouse = "Window", A_LineNumber)

CoordMode "Caret", "Screen"

AssertEq(A_CoordModeCaret, "Screen", A_LineNumber)

CoordMode "Menu", "Screen"

AssertEq(A_CoordModeMenu, "Screen", A_LineNumber)

b := false

try
{
	CoordMode "Menu", "Dummy"
}
catch
{
	b := true
}

AssertEq(b, true, A_LineNumber)

AssertEq(A_CoordModeMenu, "Screen", A_LineNumber)

CoordMode "Menu", "Window"

AssertEq(A_CoordModeMenu, "Window", A_LineNumber)

CoordMode "ToolTip", "Client" ; Reset it back for the function version of this test.
CoordMode "Pixel", "Client"
CoordMode "Mouse", "Client"
CoordMode "Caret", "Client"
CoordMode "Menu", "Client"

#if WINDOWS
AssertEq(A_RegView, 64, A_LineNumber)
	
SetRegView 32

AssertEq(A_RegView, 32, A_LineNumber)

A_RegView := "default"

AssertEq(A_RegView, 64, A_LineNumber)

SetRegView 64

AssertEq(A_RegView, 64, A_LineNumber)

SetRegView 100

AssertEq(A_RegView, 64, A_LineNumber)
#endif

#if !OSX
Assert(A_TrayMenu.Handle > 0, A_LineNumber)

AssertEq(A_IconHidden, 0, A_LineNumber)

Assert(A_IconTip.EndsWith("props-script-settings.ahk"), A_LineNumber)

AssertEq(A_IconFile, "", A_LineNumber)

AssertEq(A_IconNumber, 1, A_LineNumber)
#endif

Suspend true

AssertEq(A_IsSuspended, true, A_LineNumber)

CoordMode "Mouse", "Screen"

DllCall(CallbackCreate(SetCoordModeMouse)) ; Execute in new pseudo-thread

Assert(A_CoordModeMouse = "Screen", A_LineNumber)

DllCall(CallbackCreate(CheckCoordModeMouse))

SetCoordModeMouse() {
	Assert(A_CoordModeMouse = "Screen", A_LineNumber)

	CoordMode("Mouse", "Window")

	Assert(A_CoordModeMouse = "Window", A_LineNumber)
}

CheckCoordModeMouse() {
	Assert(A_CoordModeMouse = "Screen", A_LineNumber)
}

FileAppend "pass", "*"
