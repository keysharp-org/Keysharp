MyGui := Gui(, "KEYSHARP TESTS")
; ┌───────────────┐
; │  Add Picture  │
; └───────────────┘
MyPictureBtn := MyGui.Add("Button", "cBlue s10 x10 y10", "Display a Picture")
MyPictureBtn.OnEvent("Click", LoadPic)
;MyPic := MyGui.Add("Picture", "xp y+20 w100 h-1", A_ScriptDir "\monkey.ico")
SlugLine := MyGui.Add("Text", "cBlue s10 w200 xp y200", "Picture will display above")

MyGui.Show()

; ┌───────────┐
; │  LoadPic  │
; └───────────┘


LoadPic() {
    MyPic := MyGui.Add("Picture", "x10 y60 w100 h-1", A_ScriptDir "\monkey.ico")
    ; MyGui.Opts("+Redraw")
}