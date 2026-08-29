MyGui := Gui()

LV2_Label := MyGui.Add("Text", "w400 x10 y10","ListView testing")
LV2_Label.SetFont("cBlue s10")
; Create the ListView with two columns, Name and Size:
LV2 := MyGui.Add("ListView", "r15 w350 x10 y+5", ["Name","Size (KB)"])

Loop Files, A_MyDocuments "\*.*"
  LV2.Add(, A_LoopFileName, A_LoopFileSizeKB)

LV2_Btn1 := MyGui.Add("Button", "x10 y+5 w75 h25" ,"Selected")
LV2_Btn1.OnEvent("Click", LV_Selected)

LV2_Btn2 := MyGui.Add("Button", "x90 yp w75 h25" ,"Focused")
LV2_Btn2.OnEvent("Click", LV_Focused)

LV2_Btn3 := MyGui.Add("Button", "x170 yp wp hp +Disabled", "Column 1")
LV2_Btn3.OnEvent("Click", LV_Col1)

LV2_Btn4 := MyGui.Add("Button", "x250 yp wp hp", "Count")
LV2_Btn4.OnEvent("Click", LV_Count)

LV2_Btn5 := MyGui.Add("Button", "x10 yp+25 w100 h25", "Count Selected")
LV2_Btn5.OnEvent("Click", LV_CountSelected)

LV2_Btn6 := MyGui.Add("Button", "x115 yp w100 h25", "Row Focused")
LV2_Btn6.OnEvent("Click", LV_CountFocused)

LV2_Btn7 := MyGui.Add("Button", "x220 yp w100 h25", "Count Columns")
LV2_Btn7.OnEvent("Click", LV_CountCol)


MyGui.Show()

LV_Selected() {
    List := ListViewGetContent("Selected", LV2, MyGui)
    MsgBox(List, "LV Selected")
    List := ""
}

LV_Focused() {
    List := ListViewGetContent("Focused", LV2, MyGui)
    MsgBox(List, "LV Focused")
    List := ""
}

LV_Col1() {
    List := ListViewGetContent("Col1", LV2, MyGui)
    MsgBox(List, "LV Column 1")
    List := ""
}

LV_Count() {
    List := ListViewGetContent("Count", LV2, MyGui)
    MsgBox(List, "LV Row Count")
    List := ""
}

LV_CountSelected() {
    List := ListViewGetContent("Count Selected", LV2, MyGui)
    MsgBox(List, "LV Count Selected")
    List := ""
}

LV_CountFocused() {
    List := ListViewGetContent("Count Focused", LV2, MyGui)
    MsgBox(List, "LV Count Focused")
    List := ""
}

LV_CountCol() {
    List := ListViewGetContent("Count Col", LV2, MyGui)
    MsgBox(List, "LV Column Count")
    List := ""
}
