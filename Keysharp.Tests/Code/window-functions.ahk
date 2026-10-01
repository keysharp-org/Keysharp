#NoTrayIcon
#ErrorStdOut
#Warn All, StdOut
#Include <assert>

; The Win and Control functions on the script's own windows and controls, and on native controls made here, which
; are reached the way another program's controls are.

SetControlDelay -1
SetWinDelay -1

; A ClassNN is numbered within the whole window, so a control nested in a Tab has the name WinGetControls gives it.
nested := Gui()
nested.AddButton("x10 y10", "Outer")
tab := nested.AddTab3("x10 y40 w300 h150", ["A", "B"])
tab.UseTab(1)
nested.AddButton("x20 y80", "Inner")
tab.UseTab()
nested.Show("NoActivate w400 h250")
names := WinGetControls(nested)
for i, hwnd in WinGetControlsHwnd(nested) {
	AssertEq(ControlGetClassNN(hwnd), names[i], A_LineNumber)
	AssertEq(ControlGetHwnd(names[i], nested), hwnd, A_LineNumber)
}
AssertEq(ControlGetHwnd("Inner", nested), nested["Inner"].Hwnd, A_LineNumber)   ; a control's own text

; The process path is a drive path, which ahk_exe matches in full.
path := WinGetProcessPath(nested)
AssertEq(path, ProcessGetPath(), A_LineNumber)
Assert(SubStr(path, 2, 2) = ":\", A_LineNumber)
AssertEq(WinExist("ahk_exe " path " ahk_id " nested.Hwnd), nested.Hwnd, A_LineNumber)
AssertEq(WinGetProcessName(nested), ProcessGetName(), A_LineNumber)
Throws(() => ProcessGetPath("no such process.exe"), A_LineNumber, TargetError)

; The Last Found Window is the script's own Gui even while hidden, and WinActive() is it only while it is active.
WinExist(nested)
AssertEq(WinActive(), 0, A_LineNumber)
WinHide(nested)
AssertEq(WinExist(), nested.Hwnd, A_LineNumber)
nested.Destroy()
AssertEq(WinExist(), 0, A_LineNumber)

; A Win function acts on the one window the criteria find first, unless WinTitle is a lone ahk_group.
title := "Window functions " A_TickCount
first := Gui(, title " first"), first.Show("NoActivate w200 h100")
second := Gui(, title " second"), second.Show("NoActivate w200 h100")
Visible(g) => DllCall("IsWindowVisible", "Ptr", g.Hwnd)
WinHide(title)
AssertEq(Visible(first) + Visible(second), 1, A_LineNumber)
first.Show("NoActivate"), second.Show("NoActivate")
GroupAdd("WindowFunctionsGroup", title)
WinHide("ahk_group WindowFunctionsGroup")
AssertEq(Visible(first) + Visible(second), 0, A_LineNumber)
Throws(() => WinHide(title " missing"), A_LineNumber, TargetError)
first.Destroy(), second.Destroy()

; Waits check once for a timeout of 0, reject a negative one, and end at once on a handle naming no window.
AssertEq(WinWait(title " missing", , 0), 0, A_LineNumber)
Throws(() => WinWait(title " missing", , -1), A_LineNumber, ValueError)
AssertEq(WinWait(0), 0, A_LineNumber)
AssertEq(WinWaitClose(0), 1, A_LineNumber)

; ahk_opt overrides the thread's settings for one WinTitle. A word it does not know matches no window, while Hidden
; followed by anything but 0 or 1 is ignored, as in AutoHotkey.
optGui := Gui(, title " opt"), optGui.Show("NoActivate w200 h100")
AssertEq(WinExist("^Window functions \d+ opt$ ahk_opt RegEx"), optGui.Hwnd, A_LineNumber)
AssertEq(WinExist(title " opt ahk_opt Bogus"), 0, A_LineNumber)
AssertEq(WinExist(title " opt ahk_opt Hidden2"), optGui.Hwnd, A_LineNumber)
optGui.Hide()
AssertEq(WinExist(title " opt"), 0, A_LineNumber)
AssertEq(WinExist(title " opt ahk_opt Hidden"), optGui.Hwnd, A_LineNumber)
optGui.Destroy()

FileAppend "pass", "*"
ExitApp()
