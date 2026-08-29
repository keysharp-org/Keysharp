MyGui := Gui()


; ┌────────────┐
; │  RichEdit  │
; └────────────┘
SecondRichEdit := MyGui.Add("RichEdit", "x10 w250 h150", "Try pasting rich text and/or images here!")
SecondRichEditText := MyGui.Add("Text", "cBlue s10 w200", "ControlSetText Test (RichEdit)")
RichEditBtn1 := MyGui.Add("Button", "s8 x10 y+10", "Send Text to RichEdit")
RichEditBtn1.OnEvent("Click", SendTextToRichEdit)
RichEditBtn2 := MyGui.Add("Button", "s8 x150 yp", "Clear RichEdit")
RichEditBtn2.OnEvent("Click", ClearRichEdit)

MyGui.Show()


; ┌──────────────────────┐
; │  RichEdit Callbacks  │
; └──────────────────────┘

SendTextToRichEdit() {
RichEditVar := "
(
A line of text.
By default, the hard carriage return (Enter) between the previous line and this one will be stored.
	This line is indented with a tab; by default, that tab will also be stored.
"Quote marks" are now automatically escaped when appropriate.
)"
    ;MsgBox(EditVar)
    ControlSetText(RichEditVar, SecondRichEdit)
}
; ┌───────────────────────┐
; │  Clear Edit Callback  │
; └───────────────────────┘
ClearRichEdit() {
    ControlSetText("", SecondRichEdit)
}