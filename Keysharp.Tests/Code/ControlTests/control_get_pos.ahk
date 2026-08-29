MyGui := Gui(, "Get Positions")
Btn1 := MyGui.Add("Button", "x10 y10", "X = 10 Y = 10")
Btn2 := MyGui.Add("Button", "x10 y50", "X = 10 Y = 50")
Btn3 := MyGui.Add("Button", "x10 y90 h40", "X = 10 Y = 90")

BtnShow := MyGui.Add("Button", "x10 y130", "Get positions")
BtnShow.OnEvent("Click", GetPos)

MyGui.Show()

GetPos() {
    ControlGetPos(&x1, &y1, &w1, &h1, "X = 10 Y = 10", "Get Positions")
    ControlGetPos(&x2, &y2, &w2, &h2, "X = 10 Y = 50", "Get Positions")
    ControlGetPos(&x3, &y3, &w3, &h3, "X = 10 Y = 90", "Get Positions")
    MsgBox("X: " x1 " Y: " y1 " Width: " w1 " Height: " h1, "Button 1")
    MsgBox("X: " x2 " Y: " y2 " Width: " w2 " Height: " h2, "Button 2")
    MsgBox("X: " x3 " Y: " y3 " Width: " w3 " Height: " h3, "Button 3")
}