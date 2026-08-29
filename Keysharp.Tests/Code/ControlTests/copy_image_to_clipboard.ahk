#import KS { Clipboard, Image }

MyGui := Gui()
MyRE := MyGui.Add("RichEdit", "w400 h400")
SelectedFile := FileSelect("3", "C:\Users\" A_UserName "\Pictures\Keysharp")
Clipboard.Image := SelectedFile
ShowBtn := MyGui.Add("Button", "x10 y+10", "Paste Pic")
ShowBtn.OnEvent("Click", PastePic)
MyGui.Show()

PastePic() {
    ControlFocus(MyRE)
    Send("^v")
}
