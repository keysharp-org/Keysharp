#NoTrayIcon
#ErrorStdOut
#Warn All, StdOut
#Include <assert>

; Gui.Opt changes an existing window in place, as AutoHotkey does, so a script that keyed anything by the Hwnd
; keeps finding its window. An owned window or a tool window has no taskbar button.

Style(hwnd) => DllCall("GetWindowLongPtr", "Ptr", hwnd, "Int", -16, "Ptr")
ExStyle(hwnd) => DllCall("GetWindowLongPtr", "Ptr", hwnd, "Int", -20, "Ptr")
OwnerOf(hwnd) => DllCall("GetWindow", "Ptr", hwnd, "UInt", 4, "Ptr")   ; GW_OWNER

owner := Gui()
owned := Gui()
hwnd := owned.Hwnd
owned.Opt("+Owner" owner.Hwnd)
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

tool.Destroy()
owned.Destroy()
owner.Destroy()

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
tv.Modify(p2, "Sort")
AssertEq(tv.GetChild(p2), kidA, A_LineNumber)
AssertEq(tv.GetNext(kidA), kidB, A_LineNumber)
AssertEq(tv.GetChild(kidB), grandZ, A_LineNumber)
tv.Modify(0, "Sort")
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
lists.Destroy()

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
FileAppend "pass", "*"
ExitApp()
