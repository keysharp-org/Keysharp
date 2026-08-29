MyGui := Gui(, "Enable Control")

BtnVictim := MyGui.Add("Button", "x10 y10", "Run monkey run")
BtnVictim.OnEvent("Click", RunMonkeyRun)

EnBtn1 := MyGui.Add("Button", "x10 y+10 w80", "Disable")
EnBtn1.OnEvent("Click", Enabled)

EnBtn2 := MyGui.Add("Button", "x10 y+10 w80", "Enable")
EnBtn2.OnEvent("Click", Disabled)

EnBtn3 := MyGui.Add("Button", "x10 y+10 w80", "Toggle")
EnBtn3.OnEvent("Click", Toggled)

MyGui.Show("w200 h400")

Enabled() {
    SetControlDelay(-1)
    ControlSetEnabled(1, BtnVictim, "Enable Control")
}

Disabled() {
    SetControlDelay(-1)
    ControlSetEnabled(0, BtnVictim, "Enable Control")
}

Toggled() {
    SetControlDelay(-1)
    ControlSetEnabled(-1, BtnVictim, "Enable Control")
}

RunMonkeyRun() {
    MsgBox("Help me find a banana!", "Support hungry primates")
}