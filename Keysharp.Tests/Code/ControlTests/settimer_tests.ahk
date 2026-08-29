MyGui := Gui()

MousePosText := MyGui.Add("Text", "x10 y+10 cBlue s10 w200", "Uses SetTimer to show mouse position")
CoordText := MyGui.Add("Text", "x10 y+10 cLime", "")
;CoordText.SetFont("s20") 
SetTimer(UpdateOSD, 200)
UpdateOSD()  ; Make the first update immediate rather than waiting for the timer.

MyGui.Show()

; ┌──────────────┐
; │  Update OSD  │
; └──────────────┘


; Not used, as SetTimer is causing crashes.
UpdateOSD()
{
    Result := MouseGetPos()
    CoordText.SetFont("bold s20")
    CoordText.Text := ("X: " Result["X"] " Y: " Result["Y"])
}


