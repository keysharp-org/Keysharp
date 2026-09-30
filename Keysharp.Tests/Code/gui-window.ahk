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
