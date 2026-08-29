MyGui := Gui()

CZ_Edit1 := MyGui.Add("Edit", "x10 y10 w160 h100", "Edit controls tests")

CZ_LbBtn17 := MyGui.Add("Button", "w120 x10 y+10 h25", "Edit Line Count")
CZ_LbBtn17.OnEvent("Click", GetLineCount)

MyGui.Show()

GetLineCount() {
    LineCount := EditGetLineCount(CZ_Edit1, MyGui)
    MsgBox(LineCount, "Current Line Count")
}