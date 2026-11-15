#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#Include <assert>

; =========================
; module-body-thread.ahk
; The body of a module other than AHK and __Main runs as a thread of its own. The defaults new threads start with are
; set by the AHK module's body and __Main's; what any other module's body sets ends with that body. [Keysharp]
; =========================

#import Ks { A_Thread }
#import Plain
#import Quiet
#import Leaves
#import Leaves { After as LeavesAfter }

; ---- __Main starts with the defaults AHK's body left, and no other module's body changed them.
AssertEq(A_TitleMatchMode, 3, A_LineNumber)
AssertEq(A_CoordModeMouse, "Screen", A_LineNumber)
AssertEq(A_SendMode, "Input", A_LineNumber)

; ---- A module's body starts with the script's defaults as they are when it runs, which AHK's body has set.
AssertEq(Plain.StartedWith, "3 Input", A_LineNumber)

; ---- A new thread starts with the defaults __Main's body sets, whichever module declares the thread's function.
SetTitleMatchMode 1
SendMode "Event"
SetTimer(Plain.Record, -1)
loop 500 {
	if Plain.Seen != ""
		break
	Sleep 10
}
AssertEq(Plain.Seen, "1 Event", A_LineNumber)

; ---- The body is an auto-execute thread of its own. Its Critical, timer setting and A_LastError ended with it.
AssertEq(Quiet.Kind, "Auto", A_LineNumber)
AssertEq(Quiet.WasCritical, 0, A_LineNumber)
AssertEq(A_IsCritical, 0, A_LineNumber)
AssertEq(A_Thread.AllowTimers, 1, A_LineNumber)
AssertEq(A_LastError, 0, A_LineNumber)

; ---- Exit ended that body alone: the module's functions work, and the script carried on to here.
AssertEq(Leaves.Before, "set", A_LineNumber)
AssertEq(IsSet(LeavesAfter), 0, A_LineNumber)
AssertEq(Leaves.Use(), "usable", A_LineNumber)

FileAppend "pass", "*"

#Module AHK
SetTitleMatchMode 3
CoordMode "Mouse", "Screen"

#Module Plain
StartedWith := A_TitleMatchMode " " A_SendMode
SetTitleMatchMode "RegEx"
SendMode "InputThenPlay"
Seen := ""
Record() {
	global Seen := A_TitleMatchMode " " A_SendMode
}

#Module Quiet
#import Ks { A_Thread }
Kind := A_Thread.Kind
WasCritical := A_IsCritical
Critical
Thread "NoTimers"
A_LastError := 5

#Module Leaves
Before := "set"
Exit
After := "never"
Use() => "usable"
