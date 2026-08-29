Pics := []
; Find some pictures to display.
Loop Files, A_WinDir "\Web\Wallpaper\*.jpg", "R"
{
    ; Load each picture and add it to the array.
    Pics.Push(LoadPicture(A_LoopFileFullPath))
}
if (!Pics.Length)
{
    ; If this happens, edit the path on the Loop line above.
    MsgBox("No pictures found! Try a different directory.")
    ExitApp
}
; Add the picture control, preserving the aspect ratio of the first picture.
MyGui := Gui()
Pic := MyGui.Add("Pic", "w600 h-1 +Border", "HBITMAP:*" Pics[1]["Handle"])
MyGui.OnEvent("Escape", CloseMe)
MyGui.OnEvent("Close", CloseMe)
MyGui.Title := "Keysharp Wallpaper"
MyGui.Show()
Loop 
{
    ; Switch pictures!
    Pic.Value := "HBITMAP:*" Pics[(Mod(A_Index, Pics.Length)+1)]["Handle"]
    Sleep 3000
}

CloseMe() {
    ExitApp()
}