ThisGui := Gui(,"Control Get Focus Test")
ThisEdit := ThisGui.Add("Edit", "w400 h50")
ThisBtnOne := ThisGui.Add("Button", "x10 h25", "ButtonOne")
ThisCb := ThisGui.Add("ComboBox", "x90 w100 yp", ["Orange","Purple","Fuchsia","Lime","Aqua"])
ThisBtnTwo := ThisGui.Add("Button", "x10 y+10 h25", "ButtonTwo")
ThisChkBx := ThisGui.Add("CheckBox", "x90 w80 yp", "Click me")
FocusText := ThisGui.Add("Text", "x10 y+10 cBlue s10 w200", "Focus test")
CoordText := ThisGui.Add("Text", "x10 y+10 cRed", "")
;CoordText.SetFont("s20") 
SetTimer(UpdateOSD, 400)
UpdateOSD()  ; Make the first update immediate rather than waiting for the timer.
ThisGui.Show()



UpdateOSD()
{
    pos := MouseGetPos()
    CoordText.SetFont("bold s8")
    FocusedHwnd := ControlGetFocus("Control Get Focus Test")
    FocusedClassNN := ControlGetClassNN(FocusedHwnd)
    CoordText.Text := "Hwnd: " FocusedHwnd "`nClassNN: " FocusedClassNN

}