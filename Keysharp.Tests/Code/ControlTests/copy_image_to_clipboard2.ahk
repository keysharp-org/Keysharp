#import KS { Clipboard, Image }

MyGui := Gui(, "Copy Image to CB")
MyRE2 := MyGui.Add("RichEdit", "w400 h400")
MyPic := LoadPicture(A_ScriptDir "\Robin.png")
Clipboard.Image := "HBITMAP:" MyPic["Handle"]
ShowBtn2 := MyGui.Add("Button", "x10 y+10", "Paste Pic")
ShowBtn2.OnEvent("Click", PastePic2)
MyGui.Show()

PastePic2() {
    ControlFocus(MyRE2)
    Send("^v")
}
