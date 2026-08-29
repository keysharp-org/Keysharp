MyGui := Gui()

CZ_Text1 := MyGui.Add("Text", , "Control Functions testing")
CZ_Text1.SetFont("s10 CBlue")
CZ_Text2 := MyGui.Add("Text", "x10 y+10 w300", "Control test")
CZ_Text2.SetFont("CTeal")

CZ_ListBox := MyGui.Add("ListBox", "x10 h200 w160", ["Red","Green","Blue","Black","White", "Maroon"
    , "Purple", "Color de gos com fuig", "Weiß", "Amarillo", "красный"
    , "朱红"])

CZ_LbBtn1 := MyGui.Add("Button", "w140 x180 yp", "ControlGetClassNN")
CZ_LbBtn1.OnEvent("Click", GetClassNN)


MyGui.Show("w320" "h300")

GetClassNN() {
    ClassNN := ControlGetClassNN(CZ_ListBox, MyGui)
    MsgBox(ClassNN)
}