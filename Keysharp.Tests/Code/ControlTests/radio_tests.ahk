MyGui := Gui()
TEST_HEADER := MyGui.Add("Text", "s20 w400","Keysharp GUI Tests")
RadioText := MyGui.Add("Text", "w200 x10", "Radio group tests")
RadioText.SetFont("cBlue s10")
RadioOne := MyGui.Add("Radio", "vMyRadioGroup", "Change header font (alternate).")
RadioOne.OnEvent("Click", ChangeFont)
RadioTwo := MyGui.Add("Radio", "vMyRadioGroup", "Restore header font (alternate)")
RadioTwo.OnEvent("Click", ChangeFontBack)
RadioThree := MyGui.Add("Radio", "vMyRadioGroup", "Please click me.")
RadioThree.OnEvent("Click", RadioThreeClicked)

MyGui.Show()


ChangeFont()
{
;global TEST_HEADER
TEST_HEADER.SetFont("cBlue s14", "Comic Sans MS")
}

; ┌────────────────┐
; │  Restore font  │
; └────────────────┘
ChangeFontBack()
{
TEST_HEADER.SetFont("cBlack s8", "Arial")
MsgBox("Done", "Restoring Font")
}

; ┌───────────────────────┐
; │  RadioThree callback  │
; └───────────────────────┘

RadioThreeClicked() {
MsgBox("You clicked the last radio button.", "Radio 3 Clicked")
}