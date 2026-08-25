#NoTrayIcon
#Import "Ks" { Taskbar }
#Include <assert>

; The script-visible surface of Gui.Icon/Gui.SetIcon and Taskbar. The C# tests cover the loader; what only a
; script can reach is the binder: Taskbar is the first class here to expose one name as both a class member
; and an instance member, and Gui.Icon is a property whose getter and setter take different types.

; Code/ -> Keysharp.Tests/ -> the repository root, which is where assets/ lives.
assets := A_ScriptDir . "\..\..\assets\"
icon := assets . "Keysharp_s.ico"

; Checked rather than assumed: an unreadable path would otherwise surface as an uncaught error, and an
; uncaught error in a test script opens a modal dialog on whoever is running the suite.
Assert(FileExist(icon), A_LineNumber)

g := Gui()

; --- Gui.Icon / Gui.SetIcon -------------------------------------------------------------------------------

; The getter answers with an Image, which is what makes the documented copy-between-windows example work.
AssertEq(Type(g.Icon), "Image", A_LineNumber)

g.SetIcon(icon, , "w48")
AssertEq(Type(g.Icon), "Image", A_LineNumber)

other := Gui()
other.Icon := g.Icon                                  ; the flagship example on the Gui.Icon page
AssertEq(Type(other.Icon), "Image", A_LineNumber)
other.Destroy()

g.SetIcon("*")                                        ; back to the script icon
AssertEq(Type(g.Icon), "Image", A_LineNumber)

Throws(() => g.SetIcon(A_Temp . "\keysharp-no-such-icon.ico"), A_LineNumber)

; --- Taskbar: the class form ------------------------------------------------------------------------------

Assert(Taskbar.HasBadgeIcon = true || Taskbar.HasBadgeIcon = false, A_LineNumber)
Assert(Taskbar.IsPerWindow = true || Taskbar.IsPerWindow = false, A_LineNumber)

Taskbar.SetProgress(40)
Taskbar.SetProgress(3, 7)
Taskbar.SetProgressState("Paused")
Taskbar.SetProgress("")                               ; clears the bar, and the state with it
Taskbar.SetBadge("")

Throws(() => Taskbar.SetProgressState("8"), A_LineNumber)
Throws(() => Taskbar.SetProgressState("Normal,Error"), A_LineNumber)

; --- Taskbar: the instance form, same names on the same class ---------------------------------------------

bar := Taskbar(g.Hwnd)
AssertEq(Type(bar), "Taskbar", A_LineNumber)
bar.SetProgress(55)
bar.SetProgressState("Normal")
bar.SetProgress("")
bar.SetBadge("")

Throws(() => Taskbar(0), A_LineNumber)                ; a handle naming no window is a mistake, not a no-op

; Named arguments have to reach both forms, since they share a parameter list.
Taskbar.SetProgress(Value: 25, Maximum: 50)
bar.SetProgress(Value: 25, Maximum: 50)
Taskbar.SetProgress("")

g.Destroy()

FileAppend("pass`n", "*")
