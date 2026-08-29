MyGui := Gui()
MyListBox := MyGui.Add("ListBox", "r5 w110", ["Red","Green","Blue","Black","White"])
MyLbBtn1 := MyGui.Add("Button", "x+10 yp", "Delete White")
MyLbBtn1.OnEvent("Click", DeleteWhite)
MyLbBtn2 := MyGui.Add("Button", "x+10 yp", "Add White")
MyLbBtn2.OnEvent("Click", AddWhite)

MyGui.Show()


DeleteWhite() {

    Try 
    {
        WhiteIndex := ControlFindItem("White", MyListBox)
    }
    Catch as e  ; Handles the first error thrown by the block above.
    {
        MsgBox("An error was thrown!`nSpecifically: " e.Message, "ERROR!")
        Return
    }
    
    ControlDeleteItem(WhiteIndex, MyListBox)
}

AddWhite() {
    ControlAddItem("White", MyListBox)
}

