MyGui := Gui()

CZ_Text1 := MyGui.Add("Text", , "Control Functions testing")
CZ_Text1.SetFont("s10 CBlue")
CZ_Text2 := MyGui.Add("Text", "x10 y+10 w300", "Control test")
CZ_Text2.SetFont("CTeal")

CZ_ListBox := MyGui.Add("ListBox", "x10 h200 w160", ["Red","Green","Blue","Black","White", "Maroon"
    , "Purple", "Color de gos com fuig", "Weiß", "Amarillo", "красный"
    , "朱红"])

CZ_LbBtn1 := MyGui.Add("Button", "w140 x180 yp", "ControlGetText")
CZ_LbBtn1.OnEvent("Click", GetText)
CZ_LbBtn2 := MyGui.Add("Button", "w140 x180 yp+40", "Disabled!")
CZ_LbBtn2.Enabled := False
CZ_LbBtn3 := MyGui.Add("Button", "w140 x180 yp+80", "Hide Button")
CZ_LbBtn3.OnEvent("Click", HideButton)
CZ_LbBtn4 := MyGui.Add("Button", "w140 x180 yp+120", "Show Button")
CZ_LbBtn4.OnEvent("Click", ShowButton)
CZ_LbBtn5 := MyGui.Add("Button", "w140 x180 yp+160", "Visible?")
CZ_LbBtn5.OnEvent("Click", IsItHidden)


MyGui.Show("w320" "h300")

HideButton() {
    ControlHide(CZ_LbBtn2, MyGui)
}

ShowButton() {
    ControlShow(Cz_LbBtn2, MyGui)
}

IsItHidden() {
    Result := ControlGetVisible(Cz_LbBtn2, MyGui)
    If (Result != 0) {
        Result := "Visible"
    } Else {
        Result := "Hidden"
    }
    MsgBox(Result, "Visible or Not?")
}
