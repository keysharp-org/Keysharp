ThisGui := Gui(,"Get Index Test")
ThisCb := ThisGui.Add("ComboBox", "x10 w100 yp", ["Orange","Purple","Fuchsia","Lime","Aqua"])
IndexText := ThisGui.Add("Text", "x10 y+10 cBlue s10 w400", "Retrieve and display index of ComboBox")
ThisIndexText := ThisGui.Add("Text", "x10 y+10 cRed", "")
;CoordText.SetFont("s20") 
SetTimer(UpdateOSD, 400)
UpdateOSD()  ; Make the first update immediate rather than waiting for the timer.
ThisGui.Show()



UpdateOSD()
{
    pos := MouseGetPos()
    IndexText.SetFont("bold s8")
    CbIndex := ControlGetIndex(ThisCb)
    ThisIndexText.Text := "ComboBox index: " CbIndex

}