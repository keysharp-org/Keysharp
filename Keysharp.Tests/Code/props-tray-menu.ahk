#NoTrayIcon
#Include <assert>

; The tray menu and its tooltip belong to the script, not to the icon: #NoTrayIcon withholds only the icon,
; and both stay readable and settable without one, as they are in AutoHotkey.

AssertEq(Type(A_TrayMenu), "Menu", A_LineNumber)
Assert(A_TrayMenu.Handle > 0, A_LineNumber)

; A menu with no icon is still a menu to build on.
A_TrayMenu.Add("Custom", Noop)
Assert(A_TrayMenu.Handle > 0, A_LineNumber)

A_IconTip := "custom tip"
AssertEq(A_IconTip, "custom tip", A_LineNumber)

Noop(*) {
}

FileAppend "pass", "*"
