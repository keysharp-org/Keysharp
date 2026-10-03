#NoTrayIcon
#ErrorStdOut
#Warn All, StdOut
#Include <assert>

SetControlDelay -1
SetWinDelay -1

g := Gui("-DPIScale")
itemsMenu := Menu()
itemsMenu.Add("Item", (*) => 0)
bar := MenuBar()
bar.Add("Menu", itemsMenu)
g.MenuBar := bar
button := g.AddButton("x30 y40 w100 h30", "Coordinates")
g.Show("NoActivate x120 y130 w260 h180")
Sleep 50

; The menu's height belongs outside the content origin used for control coordinates.
g.GetClientPos(&clientX, &clientY)
WinGetPos(&screenX, &screenY, &width, &height, button)
ControlGetPos(&x, &y, &directWidth, &directHeight, button)
AssertEq(x, screenX - clientX, A_LineNumber)
AssertEq(y, screenY - clientY, A_LineNumber)
AssertEq(directWidth, width, A_LineNumber)
AssertEq(directHeight, height, A_LineNumber)
ControlGetPos(&namedX, &namedY, , , "Coordinates", g)
AssertEq(namedX, x, A_LineNumber)
AssertEq(namedY, y, A_LineNumber)

ControlMove(x + 7, , , , button)
Sleep 50
ControlGetPos(&movedX, &movedY, &movedWidth, &movedHeight, "Coordinates", g)
AssertEq(movedX, x + 7, A_LineNumber)
AssertEq(movedY, y, A_LineNumber)
AssertEq(movedWidth, width, A_LineNumber)
AssertEq(movedHeight, height, A_LineNumber)

ControlMove(, y + 9, , , "Coordinates", g)
Sleep 50
ControlGetPos(&movedX, &movedY, &movedWidth, &movedHeight, button)
AssertEq(movedX, x + 7, A_LineNumber)
AssertEq(movedY, y + 9, A_LineNumber)
AssertEq(movedWidth, width, A_LineNumber)
AssertEq(movedHeight, height, A_LineNumber)

; A top-level window's omitted axis stays in screen coordinates.
ControlGetPos(&windowX, &windowY, &windowWidth, &windowHeight, g)
ControlGetPos(&targetX, &targetY, &targetWidth, &targetHeight, , g)
AssertEq(targetX, windowX, A_LineNumber)
AssertEq(targetY, windowY, A_LineNumber)
AssertEq(targetWidth, windowWidth, A_LineNumber)
AssertEq(targetHeight, windowHeight, A_LineNumber)
WinGetPos(, &windowY, &windowWidth, &windowHeight, g)
g.GetClientPos(&clientX, &clientY)
ControlMove(12, , , , g)
Sleep 50
WinGetPos(&movedX, &movedY, &movedWidth, &movedHeight, g)
AssertEq(movedX, clientX + 12, A_LineNumber)
AssertEq(movedY, windowY, A_LineNumber)
AssertEq(movedWidth, windowWidth, A_LineNumber)
AssertEq(movedHeight, windowHeight, A_LineNumber)

g.Destroy()
FileAppend "pass", "*"
ExitApp()
