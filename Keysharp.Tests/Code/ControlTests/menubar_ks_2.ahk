MyGui := Gui(, "KEYSHARP TESTS")
MyMenuBar := MenuBar()
MyMenuBar.Add("File", FileCallBack)
MyMenuBar.Add("Edit", EditCallback)
MyGui.MenuBar := MyMenuBar
MyEdit := MyGui.Add("Edit", "w400 h400")
MyGui.Show()

FileCallback() {
    MsgBox("Clicked File")
}

EditCallback() {
    MsgBox("Clicked Edit")
}