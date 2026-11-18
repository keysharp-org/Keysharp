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

; ControlChooseString selects the first item that starts with the text, as CB_SELECTSTRING does, and returns its
; position. A miss is an error that leaves the selection alone, and so is a control that holds no list.
AssertEq(ControlChooseString("pur", lb), 4, A_LineNumber)
Throws(() => ControlChooseString("Nothing", lb), A_LineNumber, TargetError)
AssertEq(ControlGetIndex(lb), 4, A_LineNumber)
Throws(() => ControlAddItem("Extra", ed), A_LineNumber, TargetError)
Throws(() => ControlChooseString("one", ed), A_LineNumber, TargetError)

; Both raise Change, as the LBN_SELCHANGE AutoHotkey sends does. WinForms does not turn LBN_DBLCLK into DoubleClick,
; so that half is checked off Windows only.
lbChanges := 0, lbDoubleClicks := 0
OnLbChange(*) {
	global lbChanges
	lbChanges += 1
}
OnLbDoubleClick(*) {
	global lbDoubleClicks
	lbDoubleClicks += 1
}
lb.OnEvent("Change", OnLbChange)
lb.OnEvent("DoubleClick", OnLbDoubleClick)
ControlChooseIndex(2, lb)
ControlChooseIndex(2, lb)
ControlChooseString("lime", lb)
ControlChooseIndex(0, lb)
Sleep 50
AssertEq(lbChanges, 4, A_LineNumber)
#if !WINDOWS
AssertEq(lbDoubleClicks, 4, A_LineNumber)
#endif

; ControlSendText types into the script's own Edit, replacing text that EM_SETSEL selected. Windows posts the
; keystrokes, which arrive once the script sleeps.
typed := g.AddEdit("x170 y+10 w150")
ControlSendText("hello world", typed)
SendMessage(0xB1, 0, 3, ed)   ; EM_SETSEL
ControlSendText("six", ed)
Sleep 50
AssertEq(typed.Value, "hello world", A_LineNumber)
Assert(SubStr(ed.Value, 1, 3) == "six" && !InStr(ed.Value, "one"), A_LineNumber)

#if LINUX
; On Linux the keys reach the Edit as GTK key events with their modifiers, and a window passes keys on to its focused
; control.
keys := g.AddEdit("x170 y+10 w150")
ControlSend("ab{sc" Format("{:X}", GetKeySC("Left")) "}C{End}{Shift down}d{Shift up}", keys)
_WaitForText(keys, "aCbD")
AssertEq(keys.Value, "aCbD", A_LineNumber)
ControlFocus(keys)
ControlSend("{End}z", , g)
_WaitForText(keys, "aCbDz")
AssertEq(keys.Value, "aCbDz", A_LineNumber)
ControlSendText("{Left}+λ😀", , g)
_WaitForText(keys, "aCbDz{Left}+λ😀")
AssertEq(keys.Value, "aCbDz{Left}+λ😀", A_LineNumber)
keys.Opt("+ReadOnly")
ControlSendText("ignored", , g)
; A later insertion confirms the read-only send has reached the UI thread.
ControlSendText(".", typed)
Assert(_WaitForText(typed, "hello world."), A_LineNumber)
AssertEq(keys.Value, "aCbDz{Left}+λ😀", A_LineNumber)
keys.Opt("-ReadOnly")
savedKeyDelay := A_KeyDelay
SetKeyDelay -1
ControlSend("{Home}{Insert}", keys)
ControlSendText("X", keys)
_WaitForText(keys, "XCbDz{Left}+λ😀")
AssertEq(keys.Value, "XCbDz{Left}+λ😀", A_LineNumber)
ControlSend("{Insert}", keys)

SendMessage(0xB1, 0, 3, ed)
expected := "λ😀" SubStr(ed.Value, 4)
ControlSendText("λ😀", ed)
_WaitForText(ed, expected)
AssertEq(SubStr(ed.Value, 1, 3), "λ😀", A_LineNumber)
before := ed.Value
ed.Opt("+ReadOnly")
ControlSendText("`b`nignored", ed)
ControlSendText(".", typed)
Assert(_WaitForText(typed, "hello world.."), A_LineNumber)
AssertEq(ed.Value, before, A_LineNumber)

; GTK overwrite replaces a complete cursor position, including combining characters.
grapheme := g.AddEdit("x170 y+10 w150", "e" Chr(0x301) "z")
ControlSend("{Home}{Insert}", grapheme)
ControlSendText("X", grapheme)
_WaitForText(grapheme, "Xz")
AssertEq(grapheme.Value, "Xz", A_LineNumber)

ordered := g.AddEdit("x350 y10 w150 r3")
ControlFocus(ordered)
ControlSend("ab{Left}", ordered)
ControlSendText("λ", ordered)
ControlSend("{End}c", ordered)
_WaitForText(ordered, "aλbc")
AssertEq(ordered.Value, "aλbc", A_LineNumber)

; An unhandled key sent to the window reaches its event handlers after the focused Edit.
formEscapes := 0
OnFormEscape(*) {
	global formEscapes
	formEscapes += 1
}
g.OnEvent("Escape", OnFormEscape)
ControlSend("{Escape}", , g)
ControlSendText("!", , g)
Assert(_WaitForText(ordered, "aλbc!"), A_LineNumber)
escapeStart := A_TickCount
while formEscapes == 0 && A_TickCount - escapeStart < 2000
	Sleep 10
AssertEq(formEscapes, 1, A_LineNumber)
g.OnEvent("Escape", OnFormEscape, -1)

ordered.Value := ""
ControlSendText("a`nb", ordered)
_WaitForText(ordered, "a`nb")
AssertEq(StrReplace(ordered.Value, "`r"), "a`nb", A_LineNumber)
ordered.Value := ""
ControlSendText("a`rb", ordered)
_WaitForText(ordered, "a`nb")
AssertEq(StrReplace(ordered.Value, "`r"), "a`nb", A_LineNumber)
ordered.Value := ""
ControlSendText("ab`bc", ordered)
_WaitForText(ordered, "ac")
AssertEq(ordered.Value, "ac", A_LineNumber)
ordered.Value := ""
ControlSend("{U+03BB}{U+1F600}", ordered)
_WaitForText(ordered, "λ😀")
AssertEq(ordered.Value, "λ😀", A_LineNumber)
ordered.Value := ""
ControlSend("{Text}a`nb", ordered)
_WaitForText(ordered, "a`nb")
AssertEq(StrReplace(ordered.Value, "`r"), "a`nb", A_LineNumber)

noReturn := g.AddEdit("x350 y+10 w150 r3 -WantReturn")
ControlSendText("a`nb", noReturn)
_WaitForText(noReturn, "ab")
AssertEq(noReturn.Value, "ab", A_LineNumber)

numeric := g.AddEdit("x350 y+10 w150 r3 Number")
ControlSendText("a1`nλ2`t", numeric)
_WaitForText(numeric, "12")
AssertEq(numeric.Value, "12", A_LineNumber)
SendMessage(0xB1, 0, 2, numeric)
ControlSendText("λ", numeric)
ControlSendText(".", typed)
Assert(_WaitForText(typed, "hello world..."), A_LineNumber)
AssertEq(numeric.Value, "12", A_LineNumber)
ControlSendText("3", numeric)
_WaitForText(numeric, "3")
AssertEq(numeric.Value, "3", A_LineNumber)

; Rejected input must preserve a single-line selection and the character under an overwrite cursor.
numericLine := g.AddEdit("x520 y10 w150 Number", "12")
ControlSend("^a", numericLine)
ControlSendText("λ", numericLine)
ControlSendText(".", typed)
Assert(_WaitForText(typed, "hello world...."), A_LineNumber)
AssertEq(numericLine.Value, "12", A_LineNumber)
ControlSendText("3", numericLine)
Assert(_WaitForText(numericLine, "3"), A_LineNumber)
ControlSend("{Home}{Insert}", numericLine)
ControlSendText("λ", numericLine)
ControlSendText(".", typed)
Assert(_WaitForText(typed, "hello world....."), A_LineNumber)
AssertEq(numericLine.Value, "3", A_LineNumber)
ControlSendText("4", numericLine)
Assert(_WaitForText(numericLine, "4"), A_LineNumber)

numericPassword := g.AddEdit("x520 y+10 w150 Number Password", "12")
AssertEq(numericPassword.Value, "12", A_LineNumber)
ControlSend("^a", numericPassword)
ControlSendText("λ", numericPassword)
ControlSendText(".", typed)
Assert(_WaitForText(typed, "hello world......"), A_LineNumber)
AssertEq(numericPassword.Value, "12", A_LineNumber)
ControlSendText("3", numericPassword)
Assert(_WaitForText(numericPassword, "3"), A_LineNumber)
SetKeyDelay savedKeyDelay
#elif OSX
macKeys := g.AddEdit("x170 y+10 w150")
ControlSend("ab{Left}C{End}{Shift down}d{Shift up}", macKeys)
Assert(_WaitForText(macKeys, "aCbD"), A_LineNumber)
ControlFocus(macKeys)
ControlSendText("λ😀{Left}", , g)
Assert(_WaitForText(macKeys, "aCbDλ😀{Left}"), A_LineNumber)
macKeys.Opt("+ReadOnly")
ControlSendText("ignored", , g)
ControlSendText(".", typed)
Assert(_WaitForText(typed, "hello world."), A_LineNumber)
AssertEq(macKeys.Value, "aCbDλ😀{Left}", A_LineNumber)
#endif

#if WINDOWS
; A composite control's repaint preserves its native children when the GUI background changes.
paintGui := Gui("+AlwaysOnTop")
try {
	paintGui.BackColor := "White"
	paintList := paintGui.AddListView("x10 y10 w300 r2", [""])
	paintCombo := paintGui.AddComboBox("x10 y100 w200", ["Choice"])
	paintNumber := paintGui.AddUpDown("x220 y100 w90 -16", 42)
	paintGui.Show("NoActivate x20 y20 w330 h150")
	WinRedraw(paintGui)
	Sleep 50
	paintHeader := SendMessage(0x101F, 0, 0, paintList)   ; LVM_GETHEADER
	paintComboEdit := DllCall("GetWindow", "Ptr", paintCombo.Hwnd, "UInt", 5, "Ptr")   ; GW_CHILD
	paintNumberEdit := DllCall("GetWindow", "Ptr", paintNumber.Hwnd, "UInt", 5, "Ptr")
	while paintNumberEdit && !InStr(WinGetClass(paintNumberEdit), "EDIT")
		paintNumberEdit := DllCall("GetWindow", "Ptr", paintNumberEdit, "UInt", 2, "Ptr")   ; GW_HWNDNEXT
	paintChildren := [paintHeader, paintComboEdit, paintNumberEdit]
	paintColors := []
	for paintChild in paintChildren {
		Assert(paintChild != 0, A_LineNumber)
		paintColors.Push(_ChildPixel(paintChild))
		Assert(paintColors[-1] != 0xFFFFFFFF && paintColors[-1] != 0x0000FF, A_LineNumber)
	}
	for redraw in [false, true] {
		if redraw
			WinRedraw(paintGui)
		else
			paintGui.BackColor := "Red"
		Sleep 50
		for paintIndex, paintChild in paintChildren
			AssertEq(_ChildPixel(paintChild), paintColors[paintIndex], A_LineNumber)
	}
} finally paintGui.Destroy()

_ChildPixel(_Hwnd) {
	if !_Hwnd
		return 0xFFFFFFFF
	_rect := Buffer(16, 0)
	DllCall("GetClientRect", "Ptr", _Hwnd, "Ptr", _rect)
	_dc := DllCall("GetDC", "Ptr", _Hwnd, "Ptr")
	if !_dc
		return 0xFFFFFFFF
	try return DllCall("gdi32\GetPixel", "Ptr", _dc, "Int", 6, "Int", NumGet(_rect, 12, "Int") // 2, "UInt")
	finally DllCall("ReleaseDC", "Ptr", _Hwnd, "Ptr", _dc)
}

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

; Positions in a tab are the window's client coordinates, as in AutoHotkey, and a control is placed against the one
; added last anywhere in the window, also after UseTab. The controls of a Tab3 don't count towards the extents an
; X without a Y goes below. The same checks pass under AutoHotkey.
tabs := Gui("-DPIScale")
tabs.MarginX := 10, tabs.MarginY := 6
tabs.AddButton("x10 y10 w80 h24", "Before")
tabbed := tabs.AddTab3("x10 y50 w300 h250", ["One", "Two"])
firstOnPage := tabs.AddText(, "first")
; Layout containers let the window background paint beneath their children.
WS_CLIPCHILDREN := 0x02000000
AssertEq(WinGetStyle(tabs) & WS_CLIPCHILDREN, 0, A_LineNumber)
AssertEq(ControlGetStyle(tabbed) & WS_CLIPCHILDREN, 0, A_LineNumber)
page := DllCall("GetParent", "Ptr", firstOnPage.Hwnd, "Ptr")
AssertEq(WinGetStyle(page) & WS_CLIPCHILDREN, 0, A_LineNumber)
Pos(ctrl) {
	ctrl.GetPos(&x, &y, &w, &h)
	return x "," y
}
AssertEq(Pos(tabs.AddButton("x100 y200 w60 h24 Section")), "100,200", A_LineNumber)
AssertEq(Pos(tabs.AddEdit("xp yp+30 w50 h20")), "100,230", A_LineNumber)
AssertEq(Pos(tabs.AddButton("x+10 yp w60 h24")), "160,230", A_LineNumber)
AssertEq(Pos(tabs.AddButton("xs w60 h24")), "100,260", A_LineNumber)
AssertEq(Pos(tabs.AddButton("xm ym w60 h24")), "10,6", A_LineNumber)
AssertEq(Pos(tabs.AddButton("x150 w60 h24")), "150,306", A_LineNumber)
tabbed.UseTab(2)
secondOnPage := tabs.AddText(, "second")
AssertEq(Pos(secondOnPage), Pos(firstOnPage), A_LineNumber)
tabbed.UseTab()
secondOnPage.GetPos(&secondX, &secondY, , &secondH)
AssertEq(Pos(tabs.AddButton("w60 h24")), secondX "," (secondY + secondH + 6), A_LineNumber)
tabs.Destroy()

; Controls keep the z-order they were created in, the first on top, so they are listed and numbered in that order.
stack := Gui()
under := stack.AddText("x10 y10 w100 h40", "Under")
over := stack.AddEdit("x20 y20 w60 h20")
frame := stack.AddGroupBox("x10 y60 w200 h80", "Frame")
inside := stack.AddButton("x30 y85 w80 h30", "Inside")
stack.Show("NoActivate w300 h160")
AssertEq(ControlGetStyle(frame) & WS_CLIPCHILDREN, 0, A_LineNumber)
Assert(ControlGetStyle(over) & WS_CLIPCHILDREN, A_LineNumber)
hwnds := WinGetControlsHwnd(stack)
names := WinGetControls(stack)
for i, ctrl in [under, over, frame, inside] {
	AssertEq(hwnds[i], ctrl.Hwnd, A_LineNumber)
	AssertEq(ControlGetClassNN(ctrl), names[i], A_LineNumber)
}
second := stack.AddEdit("x120 y20 w60 h20")
AssertEq(ControlGetClassNN(second), WinGetClass(over) "2", A_LineNumber)

; A Text without SS_NOTIFY and a GroupBox let the mouse through to what is beneath them, such as the Edit over the Text
; and the Button inside the box. A Click handler adds SS_NOTIFY, as in AutoHotkey, and removing it keeps the style.
WM_NCHITTEST := 0x84, HTTRANSPARENT := -1, HTCLIENT := 1, SS_NOTIFY := 0x100
ScreenPoint(ctrl, dx := -1, dy := -1) {
	WinGetClientPos(&originX, &originY, , , ctrl.Gui)
	ControlGetPos(&x, &y, &w, &h, ctrl)
	return ((originX + x + (dx < 0 ? w // 2 : dx)) & 0xFFFF) | (((originY + y + (dy < 0 ? h // 2 : dy)) & 0xFFFF) << 16)
}
HitTest(ctrl) => SendMessage(WM_NCHITTEST, 0, ScreenPoint(ctrl), ctrl)
AssertEq(HitTest(under), HTTRANSPARENT, A_LineNumber)
AssertEq(HitTest(frame), HTTRANSPARENT, A_LineNumber)
AssertEq(HitTest(over), HTCLIENT, A_LineNumber)
AssertEq(HitTest(inside), HTCLIENT, A_LineNumber)
OnUnderClick(*) {
}
under.OnEvent("Click", OnUnderClick)
Assert(ControlGetStyle(under) & SS_NOTIFY, A_LineNumber)
AssertEq(HitTest(under), HTCLIENT, A_LineNumber)
under.OnEvent("Click", OnUnderClick, 0)
Assert(ControlGetStyle(under) & SS_NOTIFY, A_LineNumber)

; A right-click which passes through a Text still names it to the ContextMenu event, as the window finds the control
; under the point, also in a window with a menu bar.
menuTarget := ""
OnStackMenu(guiObj, ctrl, *) {
	global menuTarget
	menuTarget := ctrl
}
stack.OnEvent("ContextMenu", OnStackMenu)
stackMenu := Menu()
stackMenu.Add("Item", OnOpen)
stackBar := MenuBar()
stackBar.Add("File", stackMenu)
stack.MenuBar := stackBar
label := stack.AddText("x220 y10 w70 h20", "Label")
holder := DllCall("GetParent", "Ptr", label.Hwnd, "Ptr")
AssertEq(WinGetStyle(holder) & WS_CLIPCHILDREN, 0, A_LineNumber)
SendMessage(0x7B, holder, ScreenPoint(label, 5, 5), holder)   ; WM_CONTEXTMENU
menuStart := A_TickCount
while menuTarget == "" && A_TickCount - menuStart < 2000
	Sleep 10
AssertEq(menuTarget, label, A_LineNumber)
stack.Destroy()

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
#endif

#if LINUX || OSX
; The extended style carries what the toolkit knows, so AutoHotkey's WS_EX_TOPMOST test finds an always-on-top window.
exStyleGui := Gui("+AlwaysOnTop", "Window functions exstyle " A_TickCount), exStyleGui.Show("NoActivate w200 h100")
Assert(WinGetExStyle(exStyleGui) & 0x8, A_LineNumber)
exStyleGui.Destroy()
#endif

; Each nonempty descendant contributes one CRLF-terminated line.
textGui := Gui(, "Window functions text " A_TickCount)
alpha := textGui.AddText(, "Alpha")
textGui.AddEdit("w100")
textGui.AddButton(, "Beta")
textGui.AddButton(, "Beta")
textGui.AddButton("Hidden", "Secret")
textTab := textGui.AddTab3("w200 h80", ["One", "Two"])
textTab.UseTab(1)
textGui.AddButton(, "InTab1")
textTab.UseTab(2)
textGui.AddButton(, "InTab2")
textTab.UseTab()
textGui.AddText(, "Omega")
textGui.Show("NoActivate")
AssertEq(WinGetText(alpha.Hwnd), "", A_LineNumber)
AssertEq(ControlGetText(alpha), "Alpha", A_LineNumber)
DetectHiddenText false
shownText := WinGetText(textGui)
DetectHiddenText true
allText := WinGetText(textGui)
Assert(!InStr(allText, textGui.Title), A_LineNumber)
AssertEq(SubStr(allText, -2), "`r`n", A_LineNumber)
Assert(!InStr(allText, "`r`n`r`n") && SubStr(allText, 1, 2) != "`r`n", A_LineNumber)
StrReplace(allText, "Beta`r`n", , , &betaLines)
AssertEq(betaLines, 2, A_LineNumber)
Assert(InStr(allText, "Secret`r`n") && InStr(allText, "InTab2`r`n"), A_LineNumber)
Assert(InStr(shownText, "InTab1`r`n") && !InStr(shownText, "Secret") && !InStr(shownText, "InTab2"), A_LineNumber)
#if !WINDOWS
AssertEq(allText, "Alpha`r`nBeta`r`nBeta`r`nSecret`r`nInTab1`r`nInTab2`r`nOmega`r`n", A_LineNumber)
textTab.Choose(2)
DetectHiddenText false
AssertEq(WinGetText(textGui), "Alpha`r`nBeta`r`nBeta`r`nInTab2`r`nOmega`r`n", A_LineNumber)
#endif
textGui.Hide()
DetectHiddenText false
AssertEq(WinGetText(textGui), "", A_LineNumber)
DetectHiddenText true
textGui.Destroy()

; WinKill forces only a window which does not answer, and a Gui answers by hiding.
killTarget := Gui()
killTarget.Show("NA w100 h50")
WinKill(killTarget)
DetectHiddenWindows true
Assert(WinExist(killTarget), A_LineNumber)

FileAppend "pass", "*"
ExitApp()

_WaitForText(Control, Text) {
	; Targeted sends post asynchronously to the UI thread.
	Text := StrReplace(Text, "`r")
	start := A_TickCount
	while StrReplace(Control.Value, "`r") != Text && A_TickCount - start < 2000
		Sleep 10
	return StrReplace(Control.Value, "`r") == Text
}
