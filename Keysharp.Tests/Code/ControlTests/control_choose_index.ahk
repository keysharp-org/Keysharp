MyGui := Gui()

CZ_Text1 := MyGui.Add("Text", , "Control Functions testing")
CZ_Text1.SetFont("s10 CBlue")
CZ_Text2 := MyGui.Add("Text", "x10 y+10 w300", "ControlChooseIndex test")
CZ_Text2.SetFont("CTeal")

CZ_ListBox := MyGui.Add("ListBox", "x10 h200 w160", ["Red","Green","Blue","Black","White", "Maroon"
    , "Purple", "Color de gos com fuig", "Weiß", "Amarillo", "красный"
    , "朱红", "Fuchsia"])

CZ_LbBtn1 := MyGui.Add("Button", "x180 yp", "Choose Fuchsia (13)")
CZ_LbBtn1.OnEvent("Click", ChooseIndex)

MyGui.Show()

ChooseIndex() {
    ControlChooseIndex(13, CZ_ListBox)
}