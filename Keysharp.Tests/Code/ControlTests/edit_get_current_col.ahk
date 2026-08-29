MyGui := Gui()

CZ_Edit1 := MyGui.Add("Edit", "x10 y10 w160 h100", "Edit controls tests")

CZ_LbBtn14 := MyGui.Add("Button", "w120 x10 y+10 h25", "Edit column #")
CZ_LbBtn14.OnEvent("Click", GetCol)

MyGui.Show()

GetCol() {
    CurrentCol := EditGetCurrentCol(CZ_Edit1, MyGui)
    MsgBox(CurrentCol, "Current Colum No.")
}