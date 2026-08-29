MyGui := Gui()

LV2_Label := MyGui.Add("Text", "w400 x10 y10","ListView testing")
LV2_Label.SetFont("cBlue s10")
; Create the ListView with two columns, Name and Size:
LV2 := MyGui.Add("ListView", "r15 w350 x10 y+5", ["Name","Size (KB)"])

Loop Files, A_MyDocuments "\*.*"
  LV2.Add(, A_LoopFileName, A_LoopFileSizeKB)


LV2_Btn3 := MyGui.Add("Button", "x10 y+10", "Get count")
LV2_Btn3.OnEvent("Click", LV_GetCount)


MyGui.Show()



LV_GetCount() {
    ;List := ListViewGetContent("Col1", LV2, MyGui)
    MsgBox(LV2.GetCount(), "LV Column 1")
    ;List := ""
}

