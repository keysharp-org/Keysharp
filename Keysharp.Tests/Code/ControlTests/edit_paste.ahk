MyGui := Gui()

CZ_Edit1 := MyGui.Add("Edit", "x10 y10 w160 h100", "Edit controls tests")

CZ_LbBtn18 := MyGui.Add("Button", "w120 x10 y+10 h25", "Edit Paste")
CZ_LbBtn18.OnEvent("Click", EditPaster)

MyGui.Show()

EditPaster() {
    EditPasted := "How now brown cow"
    EditPaste(EditPasted, CZ_Edit1, MyGui)
}