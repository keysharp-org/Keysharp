MyGui := Gui()

CZ_Text1 := MyGui.Add("Text", , "Control Functions testing")
CZ_Text1.SetFont("s10 CBlue")
CZ_Text2 := MyGui.Add("Text", "x10 y+10 w300", "Control test")
CZ_Text2.SetFont("CTeal")

CZ_ListBox := MyGui.Add("ListBox", "x10 h200 w160", ["Red","Green","Blue","Black","White", "Maroon"
    , "Purple", "Color de gos com fuig", "Weiß", "Amarillo", "красный"
    , "朱红"])

CZ_LbBtn1 := MyGui.Add("Button", "w140 x180 yp", "ControlGetHwnd")
CZ_LbBtn1.OnEvent("Click", GetHwnd)


MyGui.Show("w320" "h300")

GetHwnd() {
    Result := ControlGetHwnd(CZ_ListBox, MyGui)
    MsgBox(Result, "Hwnd of ListBox")
}