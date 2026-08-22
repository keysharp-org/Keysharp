#NoTrayIcon
#Include <assert>

; This just tests parsing.

; Hotstrings
::btw::by the way ; basic hotstring

; Hotkeys
^a::b
^!a::
^!b::ToolTip("You pressed " . ThisHotkey)

; Remapping Hotkeys
a::b
A::b
^x::^c

MButton::Shift
XButton1::LButton
RAlt::RButton
CapsLock::Ctrl
XButton2::^LButton
LAlt::AppsKey
RCtrl::RWin
Ctrl::Alt
RWin::Return

#HotIf WinActive("ahk_class Notepad")
c::d  ; Makes the 'a' key send a 'b' key, but only in Notepad.
#HotIf  ; This puts subsequent remappings and hotkeys in effect for all windows.

#InputLevel 1
e::b
#InputLevel 0
^!f::ToolTip("You pressed " . ThisHotkey)

#InputLevel 1
; Use SendEvent so that the script's own hotkeys can be triggered.
*Numpad0::SendEvent("{Blind}{Click Down}")
*Numpad0 up::SendEvent("{Blind}{Click Up}")
#InputLevel 0
; This hotkey can be triggered by both Numpad0 and LButton:
x := 0
~LButton::x := 999

; Stacked hotkeys.
c::
d::
{
	ToolTip(A_ThisHotkey . " is being pressed")
	KeyWait(A_ThisHotkey)
	ToolTip()
}

g::
h::{
	ToolTip(A_ThisHotkey . " is being pressed")
	KeyWait(A_ThisHotkey)
	ToolTip()
	return
}

^+o::
^+e::
NamedHotkeyFunction(hk)
{
	x := 123
}

NamedHotkeyFunction("")

RAlt & j::AltTab
RAlt & k::ShiftAltTab

GroupAdd("AltTabWindow", "ahk_class MultitaskingViewFrame")  ; Windows 10
*F1::Send("{Alt down}{tab}") ; Asterisk is required in this case.
!F2::Send("{Alt up}") ; Release the Alt key, which activates the selected window.
#HotIf WinExist("ahk_group AltTabWindow")
~*Esc::Send("{Alt up}")  ; When the menu is cancelled, release the Alt key automatically.
#HotIf

; This used to fail parsing.
#h::
{
/*
*/
}

[::{
    MsgBox("hello")
}

]::{
    MsgBox("hello")
}

(::{
    MsgBox("hello")
}

)::{
    MsgBox("hello")
}

{::{
    MsgBox("hello")
}

}::{
    MsgBox("hello")
}

^;::{
    MsgBox("hello")
}

::btw::by the way
::`/`*::abcd`*`/
::bt;w::by the; way
;::omg::oh no

a := 123
testfunc(p1)
{
	global a := p1
}

testfunc "::"

AssertEq(a, "::", A_LineNumber)

FileAppend "pass", "*"

`;:::
`::;
'::;
+'::;
^;::;
:::;
+`::;
+;::`;
+w::`
t::`

ExitApp()
