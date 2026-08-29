MyGui := Gui()

CZ_Edit1 := MyGui.Add("Edit", "x10 y10 w160 h100", "Edit controls tests")

CZ_LbBtn17 := MyGui.Add("Button", "w120 x10 y+10 h25", "Selected text")
CZ_LbBtn17.OnEvent("Click", GetSelectedText)

MyGui.Show()

GetSelectedText() {
    SelectedText := EditGetSelectedText(CZ_Edit1, MyGui)
    MsgBox(SelectedText, "Selected text in Edit")
}