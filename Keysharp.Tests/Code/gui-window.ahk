#NoTrayIcon
#ErrorStdOut
#Warn All, StdOut
#Include <assert>

#if WINDOWS
; Gui.Opt changes an existing window in place, as AutoHotkey does, so a script that keyed anything by the Hwnd
; keeps finding its window. An owned window or a tool window has no taskbar button.

Style(hwnd) => DllCall("GetWindowLongPtr", "Ptr", hwnd, "Int", -16, "Ptr")
ExStyle(hwnd) => DllCall("GetWindowLongPtr", "Ptr", hwnd, "Int", -20, "Ptr")
OwnerOf(hwnd) => DllCall("GetWindow", "Ptr", hwnd, "UInt", 4, "Ptr")   ; GW_OWNER
ParentOf(hwnd) => DllCall("GetParent", "Ptr", hwnd, "Ptr")

optionsGui := Gui()
optionsGui.Opt("+dIsAbLeD")
AssertEq(Style(optionsGui.Hwnd) & 0x08000000, 0x08000000, A_LineNumber)
optionsGui.Opt("-DISABLED")
AssertEq(Style(optionsGui.Hwnd) & 0x08000000, 0, A_LineNumber)
optionsGui.Opt("Disabled")
AssertEq(Style(optionsGui.Hwnd) & 0x08000000, 0x08000000, A_LineNumber)
optionsGui.Opt("-Disabled + -")
AssertEq(Style(optionsGui.Hwnd) & 0x08000000, 0, A_LineNumber)
for windowOption in ["AlwaysOnTop", "AppMenu", "AutoScroll", "Border", "Caption", "ClickThrough",
	"Disabled", "DPIScale", "DPIResize", "LastFound", "MaximizeBox", "MinimizeBox", "OwnDialogs",
	"Resize", "Round", "SysMenu", "Theme", "ToolWindow"] {
	optionsGui.Opt("+" StrUpper(windowOption))
	optionsGui.Opt("-" StrLower(windowOption))
}
optionsGui.Opt("+mInSiZe120x80 +MaXsIzE640x480")
optionsGui.Opt("-MINSIZE -maxsize +0x40000 +E0x8")
Throws(() => optionsGui.Opt("UnknownWindowOption"), A_LineNumber, ValueError)
optionsGui.Destroy()

; The constructor parses +LastFound before the window exists and applies it once it does, so the Gui is the Last
; Found Window as soon as the constructor returns, as FindText's Gui("+LastFound").Destroy() relies on.
lastFoundGui := Gui("+LastFound")
AssertEq(WinExist(), lastFoundGui.Hwnd, A_LineNumber)
lastFoundGui.Destroy()

; A zero Show size is AutoHotkey's invisible dummy window, FindText.ahk's HotIfWinExist target. Off Windows the
; client area is clamped to one pixel: GTK maps nothing smaller, and an empty toplevel left at zero took the
; toolkit's 200x200 default.
zeroGui := Gui("-Caption +ToolWindow")
zeroGui.Show("NA x0 y0 w0 h0")
zeroGui.GetClientPos(,, &zeroWidth, &zeroHeight)
#if WINDOWS
Assert(zeroWidth <= 1 && zeroHeight <= 1, A_LineNumber)
#else
AssertEq(zeroWidth, 1, A_LineNumber)
AssertEq(zeroHeight, 1, A_LineNumber)
#endif
zeroGui.Destroy()

; A later Show keeps the dimensions it does not name. As AutoHotkey picks its show mode, a maximized window stays
; maximized unless restored or given a new size or position, which unmaximizes it first, and a minimized one is
; restored to its prior state unless an option keeps it minimized, when it takes no new size.
ClientSizeOf(g) {
	g.GetClientPos(,, &clientWidth, &clientHeight)
	return clientWidth "x" clientHeight
}
sizeGui := Gui("+Resize")
sizeGui.Add("Text",, "size")
sizeGui.Show("NoActivate w400 h300")
WinMove(,, 500, 350, sizeGui)
resizedSize := ClientSizeOf(sizeGui)
sizeGui.Show("NoActivate x50")
AssertEq(ClientSizeOf(sizeGui), resizedSize, A_LineNumber)
sizeGui.Show("NoActivate w300")
AssertEq(ClientSizeOf(sizeGui), "300x" StrSplit(resizedSize, "x")[2], A_LineNumber)
sizeGui.Show("NoActivate w320 h200")
WinMaximize(sizeGui)
sizeGui.Show("NA")
AssertEq(WinGetMinMax(sizeGui), 1, A_LineNumber)
sizeGui.Show()
AssertEq(WinGetMinMax(sizeGui), 1, A_LineNumber)
sizeGui.Show("NA h180")
AssertEq(WinGetMinMax(sizeGui), 0, A_LineNumber)
AssertEq(ClientSizeOf(sizeGui), "320x180", A_LineNumber)
WinMaximize(sizeGui)
sizeGui.Show("NoActivate")
AssertEq(WinGetMinMax(sizeGui), 0, A_LineNumber)
WinMaximize(sizeGui)
WinMinimize(sizeGui)
sizeGui.Show("NA w250")
AssertEq(WinGetMinMax(sizeGui), -1, A_LineNumber)
sizeGui.Show()
AssertEq(WinGetMinMax(sizeGui), 1, A_LineNumber)
WinRestore(sizeGui)
AssertEq(ClientSizeOf(sizeGui), "320x180", A_LineNumber)
sizeGui.Destroy()

owner := Gui()
owned := Gui()
hwnd := owned.Hwnd
owned.Opt("+oWnEr" owner.Hwnd)
AssertEq(owned.Hwnd, hwnd, A_LineNumber)
AssertEq(OwnerOf(hwnd), owner.Hwnd, A_LineNumber)
AssertEq(ExStyle(hwnd) & 0x40000, 0, A_LineNumber)   ; WS_EX_APPWINDOW
owned.Show("NoActivate w100 h50")
AssertEq(owned.Hwnd, hwnd, A_LineNumber)
AssertEq(OwnerOf(hwnd), owner.Hwnd, A_LineNumber)
owned.Opt("-Owner")
AssertEq(owned.Hwnd, hwnd, A_LineNumber)
AssertEq(OwnerOf(hwnd), 0, A_LineNumber)
AssertEq(ExStyle(hwnd) & 0x40000, 0x40000, A_LineNumber)
owned.Opt("+Owner" Format("0x{:X}", owner.Hwnd))
AssertEq(OwnerOf(hwnd), owner.Hwnd, A_LineNumber)
owned.Opt("+Owner" Format("+0x{:X}", owner.Hwnd))
AssertEq(OwnerOf(hwnd), owner.Hwnd, A_LineNumber)
Throws(() => owned.Opt("+OwnerNotAWindow"), A_LineNumber, ValueError)
AssertEq(OwnerOf(hwnd), owner.Hwnd, A_LineNumber)
Throws(() => owned.Opt("+Owner1.5"), A_LineNumber, ValueError)
AssertEq(OwnerOf(hwnd), owner.Hwnd, A_LineNumber)
scriptHwnd := A_ScriptHwnd
owned.Opt("+Owner")
AssertEq(OwnerOf(hwnd), scriptHwnd, A_LineNumber)
owned.Opt("-OwnerIgnored")
AssertEq(OwnerOf(hwnd), 0, A_LineNumber)

; An owner given at construction survives the window's creation, and an explicit WS_EX_APPWINDOW is kept.
ctor := Gui("+Owner" owner.Hwnd)
AssertEq(OwnerOf(ctor.Hwnd), owner.Hwnd, A_LineNumber)
AssertEq(ExStyle(ctor.Hwnd) & 0x40000, 0, A_LineNumber)
ctor.Destroy()
ctor := Gui("+Owner" owner.Hwnd " +E0x40000")
AssertEq(ExStyle(ctor.Hwnd) & 0x40000, 0x40000, A_LineNumber)
ctor.Destroy()
Throws(() => owned.Opt("+Owner" owned.Hwnd), A_LineNumber, ValueError)

tool := Gui()
hwnd := tool.Hwnd
tool.Opt("+ToolWindow")
AssertEq(tool.Hwnd, hwnd, A_LineNumber)
AssertEq(OwnerOf(hwnd), 0, A_LineNumber)
AssertEq(ExStyle(hwnd) & 0x40080, 0x80, A_LineNumber)   ; WS_EX_TOOLWINDOW, no WS_EX_APPWINDOW

; A window with no caption keeps the sizing border +Resize gives it, so its frame surrounds the client area.
for opts in ["-Caption +Resize", "+Resize -Caption"] {
	g := Gui(opts)
	AssertEq(Style(g.Hwnd) & 0xC40000, 0x40000, A_LineNumber)   ; WS_THICKFRAME, no WS_CAPTION
	g.Show("w300 h200 Hide")
	g.GetPos(, , &windowW, &windowH)
	g.GetClientPos(, , &clientW, &clientH)
	Assert(windowW > clientW && windowH > clientH, A_LineNumber)
	g.Opt("-Resize")
	AssertEq(Style(g.Hwnd) & 0x40000, 0, A_LineNumber)
	g.Destroy()
}

; +Parent makes the window a child within its parent's client area, which the Gui's positions are then relative to,
; and -Parent makes it a top-level window again, both keeping its Hwnd.
host := Gui("-DPIScale")
host.Show("x100 y100 w400 h300 NoActivate")
child := Gui("-Caption -DPIScale +Parent" host.Hwnd)
hwnd := child.Hwnd
AssertEq(ParentOf(hwnd), host.Hwnd, A_LineNumber)
AssertEq(Style(hwnd) & 0xC0000000, 0x40000000, A_LineNumber)   ; WS_CHILD, no WS_POPUP
child.Show("x10 y20 w100 h50 NoActivate")
child.GetPos(&x, &y, &w, &h)
AssertEq(x "," y "," w "," h, "10,20,100,50", A_LineNumber)
host.GetClientPos(&hostX, &hostY)
WinGetPos(&screenX, &screenY, , , hwnd)
AssertEq((screenX - hostX) "," (screenY - hostY), "10,20", A_LineNumber)
child.Move(30)
host.Move(150)
child.GetPos(&x, &y)
AssertEq(x "," y, "30,20", A_LineNumber)
child.Opt("-Parent")
AssertEq(child.Hwnd, hwnd, A_LineNumber)
AssertEq(ParentOf(hwnd), 0, A_LineNumber)
AssertEq(Style(hwnd) & 0x40000000, 0, A_LineNumber)
child.Opt("+Parent" host.Hwnd)
AssertEq(ParentOf(hwnd), host.Hwnd, A_LineNumber)
Throws(() => child.Opt("+Parent"), A_LineNumber, ValueError)
child.Destroy()
host.Destroy()

; Windows destroys owned and child windows with their owner, and their Guis are let go of as Destroy lets them go.
host := Gui()
owned2 := Gui("+Owner" host.Hwnd)
child := Gui("+Parent" host.Hwnd)
ownedHwnd := owned2.Hwnd, childHwnd := child.Hwnd
host.Destroy()
Assert(!GuiFromHwnd(ownedHwnd) && !GuiFromHwnd(childHwnd), A_LineNumber)
Assert(!DllCall("IsWindow", "Ptr", ownedHwnd), A_LineNumber)   ; Not made again.
Throws(() => owned2.Hwnd, A_LineNumber, Error)
owned2.Destroy()

; A window shown without a position is centered in the primary monitor's work area.
centered := Gui()
centered.Show("w200 h100 NoActivate")
WinGetPos(&x, &y, &w, &h, centered.Hwnd)
MonitorGetWorkArea(MonitorGetPrimary(), &left, &top, &right, &bottom)
AssertEq(x "," y, (left + (right - left - w) // 2) "," (top + (bottom - top - h) // 2), A_LineNumber)
centered.Destroy()

tool.Destroy()
owned.Destroy()
owner.Destroy()
#endif

; An omitted title is the script's name and an empty one is kept, as in AutoHotkey.
titled := Gui()
AssertEq(titled.Title, A_ScriptName, A_LineNumber)
titled.Destroy()
titled := Gui(, "")
AssertEq(titled.Title, "", A_LineNumber)
titled.Destroy()

; A control type's own members live on its class, as in AutoHotkey, so another type has none of them.
lists := Gui()
okButton := lists.Add("Button", , "OK")
lv := lists.Add("ListView", "w200 h100 Checked", ["Name", "Size"])
tv := lists.Add("TreeView", "w200 h100 Checked")
Throws(() => okButton.Add("x"), A_LineNumber, MethodError)
Throws(() => okButton.SetCue("x"), A_LineNumber, MethodError)
Throws(() => lv.UseTab(1), A_LineNumber, MethodError)
Throws(() => tv.SetParts(10), A_LineNumber, MethodError)
Throws(() => lv.SetFormat("yyyy"), A_LineNumber, MethodError)
cueEdit := lists.Add("Edit")
cueEdit.SetCue("cue")

; ListView rows are 1-based. Passing each GetNext result back visits every selected row once, Modify reaches the last
; row, and an option or column a call does not name keeps its state or text.
Loop 4
	lv.Add(A_Index = 1 || A_Index = 3 ? "Select" : "", "row" A_Index, A_Index)
seen := ""
r := 0
Loop 10
{
	if !(r := lv.GetNext(r))
		break
	seen .= r " "
}
AssertEq(seen, "1 3 ", A_LineNumber)
last := lv.Add(, "row5")
AssertEq(last, 5, A_LineNumber)
AssertEq(lv.Modify(last, "Select"), 1, A_LineNumber)
AssertEq(lv.GetNext(3), last, A_LineNumber)
AssertEq(lv.Modify(last + 1, "Select"), 0, A_LineNumber)
lv.Modify(2, "Check Focus")
lv.Modify(1, , , "10")
lv.Modify(2, "Vis")
AssertEq(lv.GetNext(0), 1, A_LineNumber)
AssertEq(lv.GetNext(0, "C"), 2, A_LineNumber)
AssertEq(lv.GetNext(2, "C"), 0, A_LineNumber)
AssertEq(lv.GetNext(0, "F"), 2, A_LineNumber)
AssertEq(lv.GetText(1), "row1", A_LineNumber)
AssertEq(lv.GetText(1, 2), "10", A_LineNumber)
AssertEq(lv.GetText(0, 2), "Size", A_LineNumber)
checkedRow := lv.Add("Check", "checked")
AssertEq(lv.GetNext(checkedRow - 1, "C"), checkedRow, A_LineNumber)
lv.Delete(checkedRow)

; What AutoHotkey refuses: an option that is not one, and a row or column that is not there.
Throws(() => lv.Add("Bogus", "x"), A_LineNumber, ValueError)
Throws(() => lv.ModifyCol(1, "Bogus"), A_LineNumber, ValueError)
Throws(() => lv.GetText(99), A_LineNumber, Error)
Throws(() => lv.GetText(1, 3), A_LineNumber, Error)
Throws(() => lv.DeleteCol(3), A_LineNumber, Error)
Throws(() => lv.Delete(0), A_LineNumber, ValueError)
AssertEq(lv.ModifyCol(0), 0, A_LineNumber)

lv.Modify(0, "-Select")
AssertEq(lv.GetCount("Selected"), 0, A_LineNumber)
AssertEq(lv.GetCount(), 5, A_LineNumber)
AssertEq(lv.Insert(1, , "first"), 1, A_LineNumber)
AssertEq(lv.GetText(2), "row1", A_LineNumber)
AssertEq(lv.Delete(6), 1, A_LineNumber)
AssertEq(lv.GetCount(), 5, A_LineNumber)

; An Integer column sorts by number, and Sort sorts it once.
lv.ModifyCol(2, "Integer SortDesc")
AssertEq(lv.GetText(1), "row1", A_LineNumber)
AssertEq(lv.GetText(2), "row4", A_LineNumber)
AssertEq(lv.GetText(5), "first", A_LineNumber)

; A new first column keeps the old first column's text, and the cells after it move along.
AssertEq(lv.InsertCol(1, , "New"), 1, A_LineNumber)
AssertEq(lv.GetText(0, 1), "New", A_LineNumber)
AssertEq(lv.GetText(1, 1), "row1", A_LineNumber)
AssertEq(lv.GetText(1, 2), "", A_LineNumber)
AssertEq(lv.GetText(1, 3), "10", A_LineNumber)

#if WINDOWS
; AutoHdr sizes only the column it names.
lv.ModifyCol(1, 77)
firstWidth := SendMessage(0x101D, 0, 0, lv)   ; LVM_GETCOLUMNWIDTH
lv.ModifyCol(2, "AutoHdr")
AssertEq(SendMessage(0x101D, 0, 0, lv), firstWidth, A_LineNumber)
#endif

; TreeView IDs: GetNext "Full" walks children before siblings, GetCount counts every item, 0 is the root only where
; AutoHotkey reads it so, and Bold is an item state.
p1 := tv.Add("P1")
c1 := tv.Add("C1", p1)
c2 := tv.Add("C2", p1, "Check")
g1 := tv.Add("G1", c1)
p2 := tv.Add("P2", , "Bold")
AssertEq(tv.GetCount(), 5, A_LineNumber)
walk := ""
id := 0
Loop 10
{
	if !(id := tv.GetNext(id, "Full"))
		break
	walk .= tv.GetText(id) " "
}
AssertEq(walk, "P1 C1 G1 C2 P2 ", A_LineNumber)
AssertEq(tv.GetNext(0, "Checked"), c2, A_LineNumber)
AssertEq(tv.GetNext(c2, "Checked"), 0, A_LineNumber)
AssertEq(tv.GetNext(0), p1, A_LineNumber)
AssertEq(tv.GetNext(p1), p2, A_LineNumber)
AssertEq(tv.GetChild(0), p1, A_LineNumber)
AssertEq(tv.GetChild(p1), c1, A_LineNumber)
AssertEq(tv.GetParent(g1), c1, A_LineNumber)
AssertEq(tv.GetParent(p1), 0, A_LineNumber)
AssertEq(tv.GetPrev(c2), c1, A_LineNumber)
AssertEq(tv.Get(0, "Bold"), 0, A_LineNumber)
Throws(() => tv.GetText(0), A_LineNumber, Error)
Throws(() => tv.Delete(0), A_LineNumber, ValueError)
Throws(() => tv.GetNext(p1, ""), A_LineNumber, ValueError)
Throws(() => tv.Modify(p1, "First"), A_LineNumber, ValueError)
Throws(() => tv.Add("x", , "Bogus"), A_LineNumber, ValueError)
AssertEq(tv.GetCount(), 5, A_LineNumber)
tv.Modify(c1)
AssertEq(tv.GetSelection(), c1, A_LineNumber)
tv.Modify(0)
AssertEq(tv.GetSelection(), 0, A_LineNumber)
AssertEq(tv.Get(p2, "Bold"), p2, A_LineNumber)
AssertEq(tv.Get(p1, "Bold"), 0, A_LineNumber)
tv.Modify(p2, "-Bold")
tv.Modify(p1, "Bold")
AssertEq(tv.Get(p2, "B"), 0, A_LineNumber)
AssertEq(tv.Get(p1, "B"), p1, A_LineNumber)

; Expand on Add takes effect once the item has children, and a later collapse stays.
p3 := tv.Add("P3", , "Expand")
tv.Add("C3", p3)
AssertEq(tv.Get(p3, "Expanded"), p3, A_LineNumber)
tv.Modify(p3, "-Expand")
tv.Add("C4", p3)
AssertEq(tv.Get(p3, "Expanded"), 0, A_LineNumber)
after := tv.Add("After P1", , p1)
AssertEq(tv.GetNext(p1), after, A_LineNumber)
first := tv.Add("First", , "First")
AssertEq(tv.GetNext(0), first, A_LineNumber)

; Sort sorts one level and keeps every ID.
kidB := tv.Add("b", p2)
kidA := tv.Add("a", p2)
grandZ := tv.Add("z", kidB)
grandY := tv.Add("y", kidB)
tv.Modify(p2)
tv.Modify(p2, "Sort")
AssertEq(tv.GetChild(p2), kidA, A_LineNumber)
AssertEq(tv.GetNext(kidA), kidB, A_LineNumber)
AssertEq(tv.GetChild(kidB), grandZ, A_LineNumber)
tv.Modify(0, "Sort")
AssertEq(tv.GetSelection(), p2, A_LineNumber)
AssertEq(tv.GetNext(0), after, A_LineNumber)
AssertEq(tv.GetText(p1), "P1", A_LineNumber)
AssertEq(tv.Modify(p1, "-Bold"), p1, A_LineNumber)
AssertEq(tv.Get(p1, "B"), 0, A_LineNumber)
AssertEq(tv.Delete(after), 1, A_LineNumber)
Throws(() => tv.Delete(after), A_LineNumber, Error)
AssertEq(tv.Delete(c1), 1, A_LineNumber)
AssertEq(tv.GetCount(), 11, A_LineNumber)
tv.Delete()
AssertEq(tv.GetCount(), 0, A_LineNumber)

; A StatusBar has its one part without any text, then one part per width plus the rest of the bar; a ListBox refuses
; Delete(0) rather than emptying.
sb := lists.Add("StatusBar")
Assert(sb.SetText("one"), A_LineNumber)
sb.SetParts(50, 60)
Assert(sb.SetText("three", 3), A_LineNumber)
Throws(() => sb.SetText("four", 4), A_LineNumber, Error)
Throws(() => sb.SetText("none", 0), A_LineNumber, ValueError)
#if WINDOWS
Assert(sb.SetIcon(A_ScriptDir "\Gui\monkey.ico", , 2) != 0, A_LineNumber)
#endif
lb := lists.Add("ListBox", , ["a", "b", "c"])
Throws(() => lb.Delete(0), A_LineNumber, ValueError)
lb.Delete(2)
AssertEq(ControlGetItems(lb).Length, 2, A_LineNumber)

; Row updates preserve selection and focus without rebuilding the widget.
rows := lists.AddListView("w200 h100", ["Name", "Size"])
rows.Add(, "first", "1")
rows.Add("Select Focus", "second", "2")
rows.Add(, "third", "3")
rows.Insert(1, , "before", "0")
rows.Modify(3, , "changed")
AssertEq(rows.GetNext(), 3, A_LineNumber)
AssertEq(rows.GetNext(0, "F"), 3, A_LineNumber)
rows.Delete(1)
AssertEq(rows.GetNext(), 2, A_LineNumber)
AssertEq(ListViewGetContent("Selected", rows), "changed`t2", A_LineNumber)
AssertEq(ListViewGetContent("Col2", rows), "1`n2`n3", A_LineNumber)
Throws(() => ListViewGetContent("Col3", rows), A_LineNumber, ValueError)
Throws(() => ListViewGetContent("Col0", rows), A_LineNumber, ValueError)

#if WINDOWS
multi := lists.AddListBox("Multi", ["same", "other", "same"])
multi.Text := "same"
AssertEq(multi.Value.Length, 2, A_LineNumber)
#endif

sb.Text := "first"
AssertEq(sb.Text, "first", A_LineNumber)
AssertEq(sb.Value, "", A_LineNumber)
Throws(() => sb.Value := "x", A_LineNumber, Error)

changes := 0
Changed(*) {
	global changes
	changes += 1
}
lb := lists.AddListBox(, ["Red", "Green", "Blue"])
lb.OnEvent("Change", Changed)
lb.Choose("gr")
AssertEq(lb.Value, 2, A_LineNumber)
lb.Text := "blue"
AssertEq(lb.Value, 3, A_LineNumber)
Throws(() => lb.Text := "Bl", A_LineNumber, ValueError)
Throws(() => lb.Value := [1], A_LineNumber, TypeError)
lb.Choose(0)
AssertEq(lb.Text, "", A_LineNumber)
Throws(() => lb.Choose(4), A_LineNumber, ValueError)
numeric := lists.AddListBox(, ["first", "1.0"])
numeric.Choose(1.0)
AssertEq(numeric.Value, 2, A_LineNumber)
ddl := lists.AddDropDownList("Sort", ["b", "C", "a"])
ddl.Value := 1
AssertEq(ddl.Text, "a", A_LineNumber)
ddl.Text := "c"
AssertEq(ddl.Value, 3, A_LineNumber)
cb := lists.AddComboBox(, ["Alpha", "Beta"])
cb.OnEvent("Change", Changed)
cb.Text := "Gamma"
AssertEq(cb.Value, 0, A_LineNumber)
AssertEq(cb.Text, "Gamma", A_LineNumber)
cb.Choose("be")
AssertEq(cb.Text, "Beta", A_LineNumber)
Sleep 20
AssertEq(changes, 0, A_LineNumber)

ed := lists.AddEdit("r3 w200", "one`ntwo`nthree")
AssertEq(EditGetLineCount(ed), 3, A_LineNumber)
AssertEq(EditGetLine(2, ed), "two", A_LineNumber)
Throws(() => EditGetLine(0, ed), A_LineNumber, ValueError)
Throws(() => EditGetLine(9, ed), A_LineNumber, ValueError)
num := lists.AddEdit("Number w100", "1a2")
AssertEq(num.Value, "1a2", A_LineNumber)

#if WINDOWS
; Native image lists set row height and icons, and survive control handle recreation.
CreateImageList(Width, Height) => DllCall("comctl32\ImageList_Create", "Int", Width, "Int", Height,
	"UInt", 0x21, "Int", 1, "Int", 1, "Ptr")
small := CreateImageList(1, 48)
large := CreateImageList(64, 64)
managed := IL_Create()
Assert(small != 0 && large != 0 && managed != 0, A_LineNumber)
AssertEq(rows.SetImageList(small, 1), 0, A_LineNumber)
AssertEq(rows.SetImageList(small, 1), small, A_LineNumber)
lists.Show("Hide")
rect := Buffer(16, 0)
AssertEq(SendMessage(0x100E, 0, rect, rows), 1, A_LineNumber)   ; LVM_GETITEMRECT
Assert(NumGet(rect, 12, "Int") - NumGet(rect, 4, "Int") >= 48, A_LineNumber)
icon := DllCall("LoadIcon", "Ptr", 0, "Ptr", 32512, "Ptr")
AssertEq(DllCall("comctl32\ImageList_ReplaceIcon", "Ptr", small, "Int", -1, "Ptr", icon, "Int"), 0, A_LineNumber)
rows.Modify(1, "Icon1")
item := Buffer(96, 0)
NumPut("UInt", 2, item)   ; LVIF_IMAGE
AssertEq(SendMessage(0x104B, 0, item, rows), 1, A_LineNumber)   ; LVM_GETITEMW
AssertEq(NumGet(item, 20 + 2 * A_PtrSize, "Int"), 0, A_LineNumber)
AssertEq(rows.SetImageList(large), 0, A_LineNumber)
rows.Opt("Sort")
AssertEq(SendMessage(0x1002, 0, 0, rows), large, A_LineNumber)   ; LVM_GETIMAGELIST
AssertEq(SendMessage(0x1002, 1, 0, rows), small, A_LineNumber)
AssertEq(rows.SetImageList(managed, 1), small, A_LineNumber)
managedHandle := SendMessage(0x1002, 1, 0, rows)
AssertEq(rows.SetImageList(managed, 1), managed, A_LineNumber)
AssertEq(SendMessage(0x1002, 1, 0, rows), managedHandle, A_LineNumber)
AssertEq(rows.SetImageList(small, 1), managed, A_LineNumber)
AssertEq(rows.SetImageList(0, 1), small, A_LineNumber)
AssertEq(SendMessage(0x1002, 1, 0, rows), 0, A_LineNumber)
AssertEq(rows.SetImageList(0, 0), large, A_LineNumber)
DllCall("comctl32\ImageList_Destroy", "Ptr", large)
IL_Destroy(managed)

; Shared lists outlive the GUI; an owned handle used by several slots is released once.
shared := CreateImageList(16, 16)
sharedGui := Gui()
sharedList := sharedGui.AddListView("+0x40 Checked", ["Result"])
Loop 3
	sharedList.SetImageList(shared, A_Index - 1)
sharedList.Opt("Sort")
AssertEq(SendMessage(0x1002, 2, 0, sharedList), shared, A_LineNumber)
sharedGui.Destroy()
AssertEq(DllCall("comctl32\ImageList_Destroy", "Ptr", shared), 1, A_LineNumber)
Loop 3
	rows.SetImageList(small, A_Index - 1)

; Custom drawing permits nested timers while deferring posted native messages until it returns.
Thread("Interrupt", 0)
paintMessages := 0
paintTimerRan := false
lists.OnMessage(0x8001, PaintMessage)
rows.OnNotify(-12, DrawPolling)
notification := Buffer(A_PtrSize * 3 + 4, 0)
NumPut("Ptr", rows.Hwnd, "UPtr", 0, "Int", -12, notification)
NumPut("UInt", 1, notification, A_PtrSize * 3)
AssertEq(DllCall("SendMessageW", "Ptr", rows.Hwnd, "UInt", 0x204E, "Ptr", 0, "Ptr", notification, "Ptr"), 4, A_LineNumber)
Assert(paintTimerRan, A_LineNumber)
AssertEq(paintMessages, 0, A_LineNumber)
Sleep(-1)
AssertEq(paintMessages, 1, A_LineNumber)
rows.OnNotify(-12, DrawPolling, 0)
lists.OnMessage(0x8001, PaintMessage, 0)

DrawPolling(ctrl, lParam) {
	DllCall("PostMessageW", "Ptr", ctrl.Gui.Hwnd, "UInt", 0x8001, "Ptr", 0, "Ptr", 0)
	SetTimer(PaintTimer, -1)
	start := A_TickCount
	while (A_TickCount - start < 100)
		DllCall("GetTickCount", "UInt")
	return 4
}
PaintTimer() {
	global paintTimerRan
	paintTimerRan := true
	start := A_TickCount
	while (A_TickCount - start < 25)
		DllCall("GetTickCount", "UInt")
}
PaintMessage(*) {
	global paintMessages
	paintMessages++
	return 0
}
#endif
lists.Destroy()
#if WINDOWS
AssertEq(DllCall("comctl32\ImageList_GetIconSize", "Ptr", small, "Ptr", item, "Ptr", item.Ptr + 4), 0, A_LineNumber)
#endif

#if WINDOWS
; A handler named as a method of the Gui's event sink runs as AutoHotkey runs it: looked up on the sink when the
; event fires, with the sink as this and the Gui or control first.
class EventSink {
	log := []
	Resized(g, minMax, w, h) => this.log.Push(Type(this) " " (g == win) " size")
	Clicked(ctrl, info) => this.log.Push(Type(this) " " (ctrl == btn) " click")
}

sink := EventSink()
win := Gui(, "Sink test", sink)
btn := win.AddButton(, "Go")
win.OnEvent("Size", "Resized")
btn.OnEvent("Click", "Clicked")
win.Show("w200 h100 NoActivate")
Sleep 100

sink.log := []
win.Move(, , 300, 200)
Sleep 200
Assert(sink.log.Length >= 1 && sink.log[1] == "EventSink 1 size", A_LineNumber)

SendMessage 0x00F5, 0, 0, btn   ; BM_CLICK
Sleep 200
AssertEq(sink.log[sink.log.Length], "EventSink 1 click", A_LineNumber)

; A method defined on the sink after the registration is the one called, since the name is looked up when the event fires.
sink.DefineProp("Clicked", {call: (this, ctrl, info) => this.log.Push("redefined")})
SendMessage 0x00F5, 0, 0, btn
Sleep 200
AssertEq(sink.log[sink.log.Length], "redefined", A_LineNumber)

; Removing by name removes what the same name registered.
win.OnEvent("Size", "Resized", 0)
sink.log := []
win.Move(, , 320, 220)
Sleep 200
AssertEq(sink.log.Length, 0, A_LineNumber)

win.Destroy()
#endif

#if LINUX
; Plain windows keep their Gui handles when queried by the compositor, including after a remap.
if EnvGet("XDG_SESSION_TYPE") == "wayland" && WinGetList().Length {
	queryTitle := "Plain own windows " A_TickCount
	firstQueryGui := Gui(, queryTitle)
	secondQueryGui := Gui(, queryTitle)
	try {
		firstQueryGui.Show("NoActivate w200 h100")
		secondQueryGui.Show("NoActivate w200 h100")
		queryHandles := _OwnWindowHandles(queryTitle, 2)
		AssertEq(queryHandles.Length, 2, A_LineNumber)
		Assert((queryHandles[1] == firstQueryGui.Hwnd && queryHandles[2] == secondQueryGui.Hwnd)
			|| (queryHandles[2] == firstQueryGui.Hwnd && queryHandles[1] == secondQueryGui.Hwnd), A_LineNumber)

		firstQueryGui.Hide()
		queryHandles := _OwnWindowHandles(queryTitle, 1)
		AssertEq(queryHandles.Length, 1, A_LineNumber)
		AssertEq(queryHandles[1], secondQueryGui.Hwnd, A_LineNumber)
		firstQueryGui.Show("NoActivate w200 h100")
		queryHandles := _OwnWindowHandles(queryTitle, 2)
		AssertEq(queryHandles.Length, 2, A_LineNumber)
		Assert((queryHandles[1] == firstQueryGui.Hwnd && queryHandles[2] == secondQueryGui.Hwnd)
			|| (queryHandles[2] == firstQueryGui.Hwnd && queryHandles[1] == secondQueryGui.Hwnd), A_LineNumber)
		firstQueryGui.Destroy()
		queryHandles := _OwnWindowHandles(queryTitle, 1)
		AssertEq(queryHandles.Length, 1, A_LineNumber)
		AssertEq(queryHandles[1], secondQueryGui.Hwnd, A_LineNumber)
	} finally {
		firstQueryGui.Destroy()
		secondQueryGui.Destroy()
	}
}

_OwnWindowHandles(Title, Count) {
	start := A_TickCount
	while (handles := WinGetList(Title)).Length != Count && A_TickCount - start < 3000
		Sleep 10
	return handles
}
#endif
FileAppend "pass", "*"
ExitApp()
