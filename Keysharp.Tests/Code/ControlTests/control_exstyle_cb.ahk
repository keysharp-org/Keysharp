MyGui := Gui(, "CB Set ExStyle")
MyCB := MyGui.Add("ComboBox", "x10 y+10 r10", ["Orange","Purple","Fuchsia","Lime","Aqua"])

CB_ExtendButton := MyGui.Add("Button", "h25 w80 x10 y+10", "CB Extend Ellipse")
CB_ExtendButton.OnEvent("Click", CB_Extend)

CB_RetractBtn := MyGui.Add("Button", "h25 w80 x90 yp", "CB Retract Ellipse")
CB_RetractBtn.OnEvent("Click", CB_Retract)

CB_ShowBtn := MyGui.Add("Button", "h25 w80 x170 yp ", "Show ExStyle")
CB_ShowBtn.OnEvent("Click", ShowExStyle)

MyGui.Show("w400 h200")

CB_Extend() {
    ControlSetStyleEx("+0x00000020", MyCB)
    ;MsgBox("Extend", "Extend Ellipse")
}

CB_Retract() {
    ControlSetStyleEx("-0x00000020", MyCB)
    ;MsgBox("Retract Ellipse", "Retract Ellipse")
}

ShowExStyle() {
    MsgBox("0x" Format("{1:X}", ControlGetExStyle(MyCB, MyGui.Title)), "Show ExStyle")
}