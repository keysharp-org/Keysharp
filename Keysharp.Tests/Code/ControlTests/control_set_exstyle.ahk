MyGui := Gui(, "My Window")
BtnOkay := MyGui.Add("Button", "x10 y+10 +AltSubmit", "Okay").OnEvent("Click", OkayClicked)
MyEdit := MyGui.Add("Edit", "x10 y+10 w350 h80", "Test now")
MyGui.Show("w400 h200")

OkayClicked() {
    CoordMode("Mouse", "Client")
    ControlSetExStyle("0x0010", MyEdit)
    Sleep(10)
    ;ControlSetText(pos["X"] " " pos["Y"] " " MyGui.Title " " "0x" Format("{1:X}", ControlGetExStyle(MyEdit.Hwnd)), MyEdit)
    ControlSetText(MyGui.Title " has ExStyle " "0x" Format("{1:X}", ControlGetExStyle(MyEdit.Hwnd)), MyEdit)
    Sleep(2000)
    ControlSetText("Test now", MyEdit.Hwnd)
    MyGui.Submit(False)
}