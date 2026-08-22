#NoTrayIcon
#Include <assert>

WM_MY_BROADCAST := DllCall("RegisterWindowMessage", "Str", "MyUniqueBroadcastMessage", "UInt")
HWND_BROADCAST := 0xFFFF
OnMessage(WM_MY_BROADCAST, HandleMyBroadcast)

HandleMyBroadcast(wParam, lParam, *) {
    global result++
}

result := 0
PostMessage(WM_MY_BROADCAST, 123, 456,, HWND_BROADCAST)
Sleep 200

AssertEq(result, 1, A_LineNumber)

result := 0
SendMessage(WM_MY_BROADCAST, 123, 456,, A_ScriptHwnd)

AssertEq(result, 1, A_LineNumber)

FileAppend "pass", "*"
