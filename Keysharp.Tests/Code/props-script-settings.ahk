#if OSX
#NoTrayIcon
#endif

#import KS { * }
; #Include %A_ScriptDir%/header.ahk

if (A_IsSuspended == 0) 
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

Suspend 1

if (A_IsSuspended == 1) 
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
		
Suspend false

if (A_IsCritical == 0) 
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
		
Critical true
x := A_IsCritical

if (x > 0) 
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
Critical 0
x := A_IsCritical

if (x == 0) 
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (A_TitleMatchMode == 2) 
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

SetTitleMatchMode 1

if (A_TitleMatchMode == 1) 
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

SetTitleMatchMode 2

if (A_TitleMatchMode == 2) 
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
SetTitleMatchMode 3

if (A_TitleMatchMode == 3) 
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
SetTitleMatchMode "RegEx"

if (A_TitleMatchMode == "regex") 
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

SetTitleMatchMode "dummy"

if (A_TitleMatchMode == 2) 
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (A_TitleMatchModeSpeed == "fast") 
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

SetTitleMatchMode "fast"

if (A_TitleMatchModeSpeed == "fast") 
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

SetTitleMatchMode "slow"

if (A_TitleMatchModeSpeed == "slow") 
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

SetTitleMatchMode "dummy"

if (A_TitleMatchModeSpeed == "slow") 
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (A_TitleMatchMode == 2) 
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

SetTitleMatchMode 2 ; Reset it back for the function version of this test.
SetTitleMatchMode "fast"
	
if (!A_DetectHiddenWindows) 
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

DetectHiddenWindows 0

if (!A_DetectHiddenWindows)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
DetectHiddenWindows 1

if (A_DetectHiddenWindows) 
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

DetectHiddenWindows "Off"

if (!A_DetectHiddenWindows)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

DetectHiddenWindows 1

if (A_DetectHiddenWindows) 
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
				
DetectHiddenWindows "dummy"

if (A_DetectHiddenWindows) 
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

DetectHiddenWindows 0 ; Reset it back for the function version of this test.

if (!A_DetectHiddenWindows) 
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

DetectHiddenText 0

if (!A_DetectHiddenText) 
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
DetectHiddenText 1

if (A_DetectHiddenText) 
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

DetectHiddenText false

if (!A_DetectHiddenText) 
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

DetectHiddenText true

if (A_DetectHiddenText) 
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

DetectHiddenText 0 ; Reset it back for the function version of this test.

FileEncoding "utf-8"

if (A_FileEncoding == "utf-8") 
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
FileEncoding "utf-8-raw"

if (A_FileEncoding == "utf-8-raw") 
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

FileEncoding "unicode"

if (A_FileEncoding == "utf-16") 
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

FileEncoding "utf-16"

if (A_FileEncoding == "utf-16") 
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

FileEncoding "utf-16-raw"

if (A_FileEncoding == "utf-16-raw") 
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

FileEncoding "ascii"

if (A_FileEncoding == "us-ascii") 
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

; A name which cannot be resolved raises, as it does in AutoHotkey, and leaves the setting alone.
threw := 0

try
	FileEncoding "dummy"
catch ValueError
	threw := 1

if (threw && A_FileEncoding == "us-ascii")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (A_SendLevel == 0) 
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

SendLevel 0

if (A_SendLevel == 0) 
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

SendLevel -1

if (A_SendLevel == 0) 
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
SendLevel 1

if (A_SendLevel == 1) 
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

SendLevel 100

if (A_SendLevel == 100) 
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

SendLevel 101

if (A_SendLevel == 100) 
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

SendLevel 0 ; Reset it back for the function version of this test.

if (A_StoreCapsLockMode == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

SetStoreCapsLockMode 0

if (A_StoreCapsLockMode == 0) 
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

SetStoreCapsLockMode 1

if (A_StoreCapsLockMode == 1) 
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

SetStoreCapsLockMode false

if (A_StoreCapsLockMode == 0) 
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

SetStoreCapsLockMode 1

if (A_StoreCapsLockMode == 1) 
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

SetStoreCapsLockMode "dummy"

if (A_StoreCapsLockMode == 1) 
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

SetStoreCapsLockMode 1 ; Reset it back for the function version of this test.

if (A_KeyDelay == 10) 
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (A_KeyDelayPlay == -1) 
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

SetKeyDelay 10

if (A_KeyDelay == 10) 
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

SetKeyDelay 20, 30

if (A_KeyDelay == 20) 
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

SetKeyDelay , 40

if (A_KeyDelay == 20) 
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

SetKeyDelay 50, 60, "Play"

if (A_KeyDelay == 20) 
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (A_KeyDelayPlay == 50) 
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

SetKeyDelay 10, -1 ; Reset it back for the function version of this test.
SetKeyDelay -1, -1, "Play"

if (A_WinDelay == 100) 
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

SetWinDelay 200
	
if (A_WinDelay == 200) 
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

SetWinDelay 100 ; Reset it back for the function version of this test.

if (A_ControlDelay == 20) 
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

SetControlDelay 200

if (A_ControlDelay == 200) 
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

SetControlDelay 20 ; Reset it back for the function version of this test.

if (A_MouseDelay == 10) 
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

SetMouseDelay 200

if (A_MouseDelay == 200) 
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (A_MouseDelayPlay == -1) 
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
								
SetMouseDelay 300, "Play"

if (A_MouseDelay == 200) 
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (A_MouseDelayPlay == 300) 
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
		
SetMouseDelay 10 ; Reset it back for the function version of this test.
SetMouseDelay -1, "Play"

if (A_DefaultMouseSpeed == 2)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

SetDefaultMouseSpeed 500

if (A_DefaultMouseSpeed == 500)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
		
SetDefaultMouseSpeed 2 ; Reset it back for the function version of this test.

if (A_CoordModeToolTip == "Client")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (A_CoordModePixel == "Client")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (A_CoordModeMouse == "Client")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (A_CoordModeCaret == "Client")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (A_CoordModeMenu == "Client")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

CoordMode "ToolTip", "Screen"

if (A_CoordModeToolTip == "Screen")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

CoordMode "Pixel", "Client"

if (A_CoordModePixel == "Client")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

CoordMode "Mouse", "Window"

if (A_CoordModeMouse == "Window")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

CoordMode "Caret", "Screen"

if (A_CoordModeCaret == "Screen")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

CoordMode "Menu", "Screen"

if (A_CoordModeMenu == "Screen")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

b := false

try
{
	CoordMode "Menu", "Dummy"
}
catch
{
	b := true
}

if (b == true)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (A_CoordModeMenu == "Screen")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

CoordMode "Menu", "Window"

if (A_CoordModeMenu == "Window")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

CoordMode "ToolTip", "Client" ; Reset it back for the function version of this test.
CoordMode "Pixel", "Client"
CoordMode "Mouse", "Client"
CoordMode "Caret", "Client"
CoordMode "Menu", "Client"

#if WINDOWS
if (A_RegView == 64)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
SetRegView 32

if (A_RegView == 32)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

A_RegView := "default"

if (A_RegView == 64)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

SetRegView 64

if (A_RegView == 64)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

SetRegView 100

if (A_RegView == 64)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
#endif

#if !OSX
if (A_TrayMenu.Handle > 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (A_IconHidden == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (A_IconTip.EndsWith("props-script-settings.ahk"))
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (A_IconFile == "")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (A_IconNumber == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
#endif

Suspend true

if (A_IsSuspended == true) 
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

CoordMode "Mouse", "Screen"

DllCall(CallbackCreate(SetCoordModeMouse)) ; Execute in new pseudo-thread

if (A_CoordModeMouse = "Screen")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

DllCall(CallbackCreate(CheckCoordModeMouse))

SetCoordModeMouse() {
	if (A_CoordModeMouse = "Screen")
		FileAppend "pass", "*"
	else
		FileAppend "fail", "*"

	CoordMode("Mouse", "Window")

	if (A_CoordModeMouse = "Window")
		FileAppend "pass", "*"
	else
		FileAppend "fail", "*"
}

CheckCoordModeMouse() {
	if (A_CoordModeMouse = "Screen")
		FileAppend "pass", "*"
	else
		FileAppend "fail", "*"
}