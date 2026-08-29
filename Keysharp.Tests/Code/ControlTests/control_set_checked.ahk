MyGui := Gui(, "Checkboxes")
Chk1 := MyGui.Add("CheckBox", "x10 y10 w120", "Run")
Chk2 := MyGui.Add("CheckBox", "x10 y50 w120", "Walk")
Chk3 := MyGui.Add("CheckBox", "x10 y90 h40 w120 Checked 1", "Stay home")

ChkBtn1 := MyGui.Add("Button", "x10 y130 w80", "Toggle CBox 1")
ChkBtn1.OnEvent("Click", CheckOne)

ChkBtn2 := MyGui.Add("Button", "x10 y+10 w80", "Check CBox 2")
ChkBtn2.OnEvent("Click", CheckTwo)

ChkBtn3 := MyGui.Add("Button", "x10 y+10 w80", "Uncheck CBox 3")
ChkBtn3.OnEvent("Click", CheckThree)

MyGui.Show("w200 h350")

CheckOne() {
    ControlSetChecked(-1, Chk1)
}

CheckTwo() {
    ControlSetChecked(1, Chk2)
}

CheckThree() {
    ControlSetChecked(0, Chk3)
}