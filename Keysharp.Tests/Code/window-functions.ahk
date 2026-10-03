#NoTrayIcon
#ErrorStdOut
#Warn All, StdOut
#Include <assert>

; The Win and Control functions on the script's own windows and controls, and on native controls made here, which
; are reached the way another program's controls are.

SetControlDelay -1
SetWinDelay -1
WS_CHILD := 0x40000000, WS_VISIBLE := 0x10000000, WS_DISABLED := 0x08000000
BS_AUTOCHECKBOX := 3, LVS_REPORT := 1

longItem := ""
Loop 300
	longItem .= Chr(65 + Mod(A_Index, 26))

g := Gui()
lb := g.AddListBox("x10 y10 w150 r10", ["Orange", "", longItem, "Purple", "Lime", "Aqua", "Navy", "Teal", "Gray", "Silver"])
cb := g.AddComboBox("x170 y10 w150", ["Red", longItem])
ed := g.AddEdit("x170 y+10 w150 r3", "one`r`ntwo`r`nthree")
g.Show("NoActivate w700 h400")

; LB_GETTEXT and CB_GETLBTEXT write the whole item, so every item is read, an empty one or one of 300 characters.
items := ControlGetItems(lb)
AssertEq(items.Length, 10, A_LineNumber)
AssertEq(items[2], "", A_LineNumber)
AssertEq(items[3], longItem, A_LineNumber)
AssertEq(ControlGetItems(cb)[2], longItem, A_LineNumber)
ControlChooseIndex(2, cb)
AssertEq(ControlGetChoice(cb), longItem, A_LineNumber)

; The script's own list controls answer as any other: the search ignores case, and a missing item or index is an error.
AssertEq(ControlFindItem("PURPLE", lb), 4, A_LineNumber)
Throws(() => ControlFindItem("Nothing", lb), A_LineNumber, TargetError)
AssertEq(ControlAddItem("Extra", cb), 3, A_LineNumber)
ControlDeleteItem(3, cb)
AssertEq(ControlGetItems(cb).Length, 2, A_LineNumber)
Throws(() => ControlDeleteItem(9, cb), A_LineNumber, TargetError)

; "xN yN" clicks that point of the window, here in the fourth row, rather than the middle of the control there.
ControlGetPos(&lbX, &lbY, , , lb)
rowHeight := SendMessage(0x1A1, 0, 0, lb)   ; LB_GETITEMHEIGHT
ControlClick("x" (lbX + 10) " y" (lbY + 2 + rowHeight * 3 + rowHeight // 2), g, , , , "NA")
Sleep 50
AssertEq(ControlGetIndex(lb), 4, A_LineNumber)

AssertEq(EditGetLine(2, ed), "two", A_LineNumber)
Throws(() => EditGetLine(0, ed), A_LineNumber, ValueError)
Throws(() => EditGetLine(9, ed), A_LineNumber, ValueError)

; BM_GETCHECK reports a checked box as checked whatever else it is, here disabled.
box := DllCall("CreateWindowEx", "UInt", 0, "Str", "Button", "Str", "Box", "UInt", WS_CHILD | WS_VISIBLE | WS_DISABLED | BS_AUTOCHECKBOX, "Int", 500, "Int", 100, "Int", 100, "Int", 20, "Ptr", g.Hwnd, "Ptr", 0, "Ptr", 0, "Ptr", 0, "Ptr")
SendMessage(0xF1, 1, 0, box)   ; BM_SETCHECK
AssertEq(ControlGetChecked(box), 1, A_LineNumber)
SendMessage(0xF1, 0, 0, box)
AssertEq(ControlGetChecked(box), 0, A_LineNumber)

; A ListView of no Gui is read through its process's memory, as another program's is, and that leaves no handle open.
icc := Buffer(8)
NumPut("UInt", 8, icc, 0)
NumPut("UInt", 1, icc, 4)   ; ICC_LISTVIEW_CLASSES
DllCall("comctl32\InitCommonControlsEx", "Ptr", icc)
lv := DllCall("CreateWindowEx", "UInt", 0, "Str", "SysListView32", "Str", "", "UInt", WS_CHILD | WS_VISIBLE | LVS_REPORT, "Int", 500, "Int", 150, "Int", 150, "Int", 100, "Ptr", g.Hwnd, "Ptr", 0, "Ptr", 0, "Ptr", 0, "Ptr")
column := Buffer(56, 0)   ; LVCOLUMNW
NumPut("UInt", 2, column, 0)   ; LVCF_WIDTH
NumPut("Int", 100, column, 8)
SendMessage(0x1061, 0, column, lv)   ; LVM_INSERTCOLUMNW
row := Buffer(88, 0)   ; LVITEMW
for i, rowText in ["first", "second"] {
	NumPut("UInt", 1, row, 0)   ; LVIF_TEXT
	NumPut("Int", i - 1, row, 4)
	NumPut("Ptr", StrPtr(rowText), row, A_PtrSize = 8 ? 24 : 20)
	SendMessage(0x104D, 0, row, lv)   ; LVM_INSERTITEMW
}

rows := StrSplit(ListViewGetContent(, lv), "`n", "`r")
AssertEq(rows.Length, 2, A_LineNumber)
AssertEq(rows[1], "first", A_LineNumber)
AssertEq(rows[2], "second", A_LineNumber)
proc := DllCall("GetCurrentProcess", "Ptr")
before := 0, after := 0
DllCall("GetProcessHandleCount", "Ptr", proc, "UInt*", &before)
Loop 50
	ListViewGetContent(, lv)
DllCall("GetProcessHandleCount", "Ptr", proc, "UInt*", &after)
Assert(after - before < 10, A_LineNumber)
; Coordinates of a native child are relative to the selected target, or the top-level client for a direct handle.
panel := DllCall("CreateWindowEx", "UInt", 0, "Str", "Static", "Str", "NativeContainer", "UInt", WS_CHILD | WS_VISIBLE, "Int", 400, "Int", 280, "Int", 200, "Int", 80, "Ptr", g.Hwnd, "Ptr", 0, "Ptr", 0, "Ptr", 0, "Ptr")
child := DllCall("CreateWindowEx", "UInt", 0, "Str", "Button", "Str", "NestedNative", "UInt", WS_CHILD | WS_VISIBLE, "Int", 3, "Int", 4, "Int", 100, "Int", 20, "Ptr", panel, "Ptr", 0, "Ptr", 0, "Ptr", 0, "Ptr")
ControlGetPos(&childX, &childY, &childWidth, &childHeight, child)
AssertEq(childX, 403, A_LineNumber)
AssertEq(childY, 284, A_LineNumber)
ControlGetPos(&localX, &localY, , , "NestedNative", panel)
AssertEq(localX, 3, A_LineNumber)
AssertEq(localY, 4, A_LineNumber)
ControlGetPos(&panelX, &panelY, , , , panel)
AssertEq(panelX, 400, A_LineNumber)
AssertEq(panelY, 280, A_LineNumber)
AssertEq(ControlGetClassNN("NestedNative", panel), "Button1", A_LineNumber)
AssertEq(ControlGetClassNN(child, "no matching window"), ControlGetClassNN(child), A_LineNumber)
ControlMove(413, , , , child)
ControlGetPos(&childX, &childY, &movedWidth, &movedHeight, child)
AssertEq(childX, 413, A_LineNumber)
AssertEq(childY, 284, A_LineNumber)
AssertEq(movedWidth, childWidth, A_LineNumber)
AssertEq(movedHeight, childHeight, A_LineNumber)
ControlMove(, 9, , , "NestedNative", panel)
ControlGetPos(&localX, &localY, , , "NestedNative", panel)
AssertEq(localX, 13, A_LineNumber)
AssertEq(localY, 9, A_LineNumber)

; Resizing an owned top-level window keeps its screen position, since its owner is not a coordinate parent.
owned := Gui("+Owner" g.Hwnd)
owned.Show("NoActivate x100 y120 w200 h100")
WinGetPos(&ownedX, &ownedY, &ownedWidth, &ownedHeight, owned)
ControlMove(, , ownedWidth + 20, , owned)
WinGetPos(&resizedX, &resizedY, &resizedWidth, &resizedHeight, owned)
AssertEq(resizedX, ownedX, A_LineNumber)
AssertEq(resizedY, ownedY, A_LineNumber)
AssertEq(resizedWidth, ownedWidth + 20, A_LineNumber)
AssertEq(resizedHeight, ownedHeight, A_LineNumber)
owned.Destroy()
g.Destroy()

; A MenuSelect path that names nothing selects nothing, rather than the last item it did find.
opens := 0
OnOpen(*) {
	global opens
	opens += 1
}

moreMenu := Menu()
moreMenu.Add("Leaf", OnOpen)
fileMenu := Menu()
fileMenu.Add("Open", OnOpen)
fileMenu.Add("More", moreMenu)
bar := MenuBar()
bar.Add("File", fileMenu)
menuGui := Gui()
menuGui.MenuBar := bar
menuGui.Show("NoActivate w200 h100")
Throws(() => MenuSelect(menuGui, , "File", "Bogus"), A_LineNumber, ValueError)
Throws(() => MenuSelect(menuGui, , "File", "More", "Bogus"), A_LineNumber, ValueError)
Throws(() => MenuSelect(menuGui, , "File", "Open", "Extra"), A_LineNumber, ValueError)
Throws(() => MenuSelect(menuGui, , "0&", "1&", "1&"), A_LineNumber, ValueError)   ; past the system menu's first item
Sleep 50
AssertEq(opens, 0, A_LineNumber)
MenuSelect(menuGui, , "File", "More", "Leaf")
Sleep 50
AssertEq(opens, 1, A_LineNumber)
menuGui.Destroy()

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
inner := nested["Inner"]
ControlGetPos(&innerX, &innerY, , , "Inner", nested)
ControlGetPos(&directX, &directY, , , inner)
AssertEq(directX, innerX, A_LineNumber)
AssertEq(directY, innerY, A_LineNumber)
ControlMove(innerX + 7, innerY + 5, , , inner)
ControlGetPos(&directX, &directY, , , "Inner", nested)
AssertEq(directX, innerX + 7, A_LineNumber)
AssertEq(directY, innerY + 5, A_LineNumber)

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

; A style value is read as in AutoHotkey: a number replaces the style, and +, - or ^ before one adds, removes or
; toggles its bits. A blank value is an error.
styleGui := Gui(, title " style"), styleButton := styleGui.AddButton(, "B"), styleGui.Show("NoActivate w200 h100")
style := WinGetStyle(styleGui)
WinSetStyle("^" WS_DISABLED, styleGui)
AssertEq(WinGetStyle(styleGui), style | WS_DISABLED, A_LineNumber)
WinSetStyle("-0x08000000", styleGui)
AssertEq(WinGetStyle(styleGui), style, A_LineNumber)
WinSetStyle(style | WS_DISABLED, styleGui)
AssertEq(WinGetStyle(styleGui), style | WS_DISABLED, A_LineNumber)
; A negative number is the remove operator, since its text starts with "-".
WinSetStyle(-WS_DISABLED, styleGui)
AssertEq(WinGetStyle(styleGui), style, A_LineNumber)
Throws(() => WinSetStyle("", styleGui), A_LineNumber, ValueError)
ControlSetStyle("+" WS_DISABLED, styleButton)
Assert(ControlGetStyle(styleButton) & WS_DISABLED, A_LineNumber)
ControlSetStyle("-" WS_DISABLED, styleButton)
Assert(!(ControlGetStyle(styleButton) & WS_DISABLED), A_LineNumber)
styleGui.Destroy()

FileAppend "pass", "*"
ExitApp()
