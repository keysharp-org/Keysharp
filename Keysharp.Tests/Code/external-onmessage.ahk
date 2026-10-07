#NoTrayIcon
#ErrorStdOut
#Warn All, StdOut
#Include <assert>

DetectHiddenWindows true
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

result := 0
reply := SendMessage(WM_MY_BROADCAST, 321, 654,, A_ScriptHwnd)

AssertEq(result, 1, A_LineNumber)
AssertEq(reply, 37, A_LineNumber)
AssertEq(received[1], 321, A_LineNumber)
AssertEq(received[2], 654, A_LineNumber)
AssertEq(received[3], WM_MY_BROADCAST, A_LineNumber)
AssertEq(received[4], A_ScriptHwnd, A_LineNumber)

OnMessage(WM_MY_BROADCAST, HandleMyBroadcast, 0)

FileAppend "pass", "*"
