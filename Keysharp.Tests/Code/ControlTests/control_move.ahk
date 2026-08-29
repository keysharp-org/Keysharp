MyGui := Gui(, "Move Control")
Btn1 := MyGui.Add("Button", "x10 y10", "X = 10 Y = 10")
Btn2 := MyGui.Add("Button", "x10 y50", "X = 10 Y = 50")
Btn3 := MyGui.Add("Button", "x10 y90 h40", "X = 10 Y = 90")

BtnMove := MyGui.Add("Button", "x10 y130", "Move Btn 1")
BtnMove.OnEvent("Click", MoveControl)

MyGui.Show("w200 h500")

MoveControl() {
    ControlMove(10, 300, 86, 40, Btn1)
}