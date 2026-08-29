MyGui := Gui(, "KEYSHARP TESTS")
ButtonOne := MyGui.Add("Button", "w200", "Get ClassNNs")
ButtonOne.OnEvent("Click", EnumCtrls)

MyEdit := MyGui.Add("Edit", "x10 h200 w200")

MyGui.Show()

EnumCtrls() {

    for GuiCtrlObj in MyGui {
        theNN := ControlGetClassNN(GuiCtrlObj, MyGui)
        theMsg.= "Control #" A_Index " is " theNN "`n"
    }
    MyEdit.Value := theMsg
}