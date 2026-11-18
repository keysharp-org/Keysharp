#NoTrayIcon
#ErrorStdOut
#Warn All, StdOut
#Import Ks { RealThread, Await, A_RealThread }
#Include <assert>

; The windows and monitors below keep the script running, so an uncaught error ends it rather than leaving it idle.
OnError(ExitOnError)
ExitOnError(e, *) {
    FileAppend "fail: " e.Message " at line " e.Line "`n", "*"
    ExitApp 1
}

DetectHiddenWindows true

#if WINDOWS
WM_MY_BROADCAST := DllCall("RegisterWindowMessage", "Str", "MyUniqueBroadcastMessage", "UInt")
HWND_BROADCAST := 0xFFFF
OnMessage(WM_MY_BROADCAST, HandleMyBroadcast)

HandleMyBroadcast(wParam, lParam, Msg, Hwnd) {
    global result++
    global received := [wParam, lParam, Msg, Hwnd]
    return 37
}

result := 0, received := []
PostMessage(WM_MY_BROADCAST, 123, 456,, HWND_BROADCAST)
Sleep 200

AssertEq(result, 1, A_LineNumber)
AssertEq(received[1], 123, A_LineNumber)
AssertEq(received[2], 456, A_LineNumber)
AssertEq(received[3], WM_MY_BROADCAST, A_LineNumber)
AssertEq(received[4], A_ScriptHwnd, A_LineNumber)

OnMessage(WM_MY_BROADCAST, HandleMyBroadcast, 0)
#endif

; Messages the script sends or posts to its own windows and controls reach its OnMessage monitors, Windows or not.
WM_APP := 0x8000, WM_CLOSE := 0x10, EM_SETSEL := 0xB1
wideParam := A_PtrSize == 8 ? 0x123456789AB : 0x1234567
zeroPtr := {Ptr: 0}
AssertEq(SendMessage(0, zeroPtr, zeroPtr, , A_ScriptHwnd), 0, A_LineNumber)
PostMessage(0, zeroPtr, zeroPtr, , A_ScriptHwnd)
AssertEq(SendMessage(0, "", "", , A_ScriptHwnd), 0, A_LineNumber)
PostMessage(0, "", "", , A_ScriptHwnd)
for item in [[1.5, TypeError], [{}, PropertyError], [{Ptr: 1.5}, TypeError], [{Ptr: "5"}, TypeError]] {
    value := item[1], errorType := item[2]
    Throws(() => SendMessage(0, value, 0, , A_ScriptHwnd), A_LineNumber, errorType)
    Throws(() => SendMessage(0, 0, value, , A_ScriptHwnd), A_LineNumber, errorType)
    Throws(() => PostMessage(0, value, 0, , A_ScriptHwnd), A_LineNumber, errorType)
    Throws(() => PostMessage(0, 0, value, , A_ScriptHwnd), A_LineNumber, errorType)
}
global calls := []
Record(wParam, lParam, Msg, Hwnd) {
    calls.Push([wParam, lParam, Msg, Hwnd, A_EventInfo, A_RealThread == RealThread.Main])
    return 37
}
OnMessage(WM_APP, Record)

g := Gui()
ed := g.AddEdit("w200 r3", "one two three")
g.Show("NoActivate w300 h120")

; A sent message waits for the callback's result, and A_EventInfo is 0. Both parameters are pointer-sized.
AssertEq(SendMessage(WM_APP, wideParam, -1, , A_ScriptHwnd), 37, A_LineNumber)
AssertEq(calls.Length, 1, A_LineNumber)
AssertEq(calls[1][1], wideParam, A_LineNumber)
AssertEq(calls[1][2], -1, A_LineNumber)
AssertEq(calls[1][3], WM_APP, A_LineNumber)
AssertEq(calls[1][4], A_ScriptHwnd, A_LineNumber)
AssertEq(calls[1][5], 0, A_LineNumber)
AssertEq(SendMessage(WM_APP, 1, 2, , g), 37, A_LineNumber)
AssertEq(calls[2][4], g.Hwnd, A_LineNumber)

; A message sent rather than posted to a control reaches only the control's own monitors.
ControlMessage(ctrl, wParam, lParam, Msg) => wParam + lParam
ed.OnMessage(WM_APP, ControlMessage)
AssertEq(SendMessage(WM_APP, 5, 6, ed), 11, A_LineNumber)
AssertEq(SendMessage(WM_APP, "", {Ptr: 6}, ed), 6, A_LineNumber)
AssertEq(SendMessage(WM_APP, {Ptr: 6}, "", ed), 6, A_LineNumber)
AssertEq(calls.Length, 2, A_LineNumber)
ed.OnMessage(WM_APP, ControlMessage, 0)

; A monitor returning 0 claims the message, which gets no default processing; an empty return lets it through.
global claim := true
ClaimSetSel(*) => claim ? 0 : ""
SendMessage(EM_SETSEL, 0, 0, ed)
ed.OnMessage(EM_SETSEL, ClaimSetSel)
AssertEq(SendMessage(EM_SETSEL, 0, 3, ed), 0, A_LineNumber)
AssertEq(EditGetSelectedText(ed), "", A_LineNumber)
claim := false
SendMessage(EM_SETSEL, 0, 3, ed)
AssertEq(EditGetSelectedText(ed), "one", A_LineNumber)
ed.OnMessage(EM_SETSEL, ClaimSetSel, 0)

; Off Windows, a global monitor destroying the target suppresses its owner handlers.
#if !WINDOWS
temporaryGui := Gui()
temporaryGui.OnMessage(WM_APP + 1, (*) => 99)
destroyTarget := (*) => temporaryGui.Destroy()
OnMessage(WM_APP + 1, destroyTarget)
AssertEq(SendMessage(WM_APP + 1, 0, 0, , temporaryGui), 0, A_LineNumber)
OnMessage(WM_APP + 1, destroyTarget, 0)
Throws(() => temporaryGui.Hwnd, A_LineNumber, Error)
#endif

; A control monitor can destroy its target: the reply changes from the live text length to zero.
WM_GETTEXTLENGTH := 0x0E
temporaryGui := Gui()
temporaryEdit := temporaryGui.AddEdit("w100 r2", "text")
temporaryGui.Show("NoActivate")
AssertEq(SendMessage(WM_GETTEXTLENGTH, 0, 0, temporaryEdit), 4, A_LineNumber)
temporaryEdit.OnMessage(WM_GETTEXTLENGTH, (*) => temporaryGui.Destroy())
AssertEq(SendMessage(WM_GETTEXTLENGTH, 0, 0, temporaryEdit), 0, A_LineNumber)
Throws(() => temporaryGui.Hwnd, A_LineNumber, Error)
Throws(() => temporaryEdit.Hwnd, A_LineNumber, Error)

global closes := 0
OnClose(*) {
    global closes++
}
g.OnEvent("Close", OnClose)
ClaimClose(wParam, lParam, Msg, Hwnd) => claim ? 0 : ""
OnMessage(WM_CLOSE, ClaimClose)
claim := true
AssertEq(SendMessage(WM_CLOSE, 0, 0, , g), 0, A_LineNumber)
Sleep 50
AssertEq(closes, 0, A_LineNumber)
claim := false
SendMessage(WM_CLOSE, 0, 0, , g)
WaitFor(() => closes, 1)
AssertEq(closes, 1, A_LineNumber)
OnMessage(WM_CLOSE, ClaimClose, 0)
g.Show("NoActivate")

; A posted message is delivered later, in the order posted, with the time it was posted in A_EventInfo, so a message
; sent after them is answered first. One posted to a control reaches the global monitors too.
calls := []
PostMessage(WM_APP, 1, 0, , g)
PostMessage(WM_APP, 2, 0, ed)
PostMessage(WM_APP, wideParam, -2, , g)
AssertEq(calls.Length, 0, A_LineNumber)
AssertEq(SendMessage(WM_APP, 3, 0, , g), 37, A_LineNumber)
WaitFor(() => calls.Length, 4)
AssertEq(calls.Length, 4, A_LineNumber)
AssertEq(calls[1][1], 3, A_LineNumber)
AssertEq(calls[1][5], 0, A_LineNumber)
AssertEq(calls[2][1], 1, A_LineNumber)
AssertEq(calls[3][1], 2, A_LineNumber)
AssertEq(calls[3][4], ed.Hwnd, A_LineNumber)
AssertEq(calls[4][1], wideParam, A_LineNumber)
AssertEq(calls[4][2], -2, A_LineNumber)
Assert(calls[2][5] != 0, A_LineNumber)

; A window destroyed before its posted message arrives gets nothing, so no monitor sees it.
gone := Gui()
PostMessage(WM_APP, 99, 0, , gone)
gone.Destroy()
PostMessage(WM_APP, 100, 0, , g)
WaitFor(() => calls.Length, 5)
AssertEq(calls.Length, 5, A_LineNumber)
AssertEq(calls[5][1], 100, A_LineNumber)

; A message sent from another real thread is delivered on the script's main thread, which returns its result.
worker := RealThread(() => SendMessage(WM_APP, 7, 8, , A_ScriptHwnd, , , , 0))
AssertEq(Await(worker.Task, 5000), 37, A_LineNumber)
AssertEq(calls[6][1], 7, A_LineNumber)
Assert(calls[6][6], A_LineNumber)

; ControlGetFocus identifies the focused control from either thread. The later of two controls is focused first, so the
; answer cannot be the one a window focuses by default.
second := g.AddEdit("w200", "second")
g.Show()
for target in [second, ed] {
    target.Focus()
    WaitFor(() => ControlGetFocus(g), target.Hwnd)
    AssertEq(ControlGetFocus(g), target.Hwnd, A_LineNumber)
    worker := RealThread(() => ControlGetFocus(g))
    AssertEq(Await(worker.Task, 5000), target.Hwnd, A_LineNumber)
}

OnMessage(WM_APP, Record, 0)
g.Destroy()
FileAppend "pass", "*"
ExitApp

WaitFor(Read, Value) {
    start := A_TickCount
    while Read() != Value && A_TickCount - start < 2000
        Sleep 10
}
