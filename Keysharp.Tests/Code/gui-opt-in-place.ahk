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
FileAppend "pass", "*"
