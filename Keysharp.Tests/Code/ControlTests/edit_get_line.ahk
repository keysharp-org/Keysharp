MyGui := Gui()

CZ_Edit1 := MyGui.Add("Edit", "x10 y10 w160 h100", "Edit controls tests")

CZ_LbBtn16 := MyGui.Add("Button", "w120 x10 y+10 h25", "Edit Line Text")
CZ_LbBtn16.OnEvent("Click", GetLineText)

MyGui.Show()

GetLineText() {
    CurrentLine := EditGetCurrentLine(CZ_Edit1, MyGui)
    Try
    {
        CurrentLineText := EditGetLine(CurrentLine, CZ_Edit1, MyGui)
        MsgBox(CurrentLineText, "Current Line Text")
    }
    catch as e  ; Handles the first error thrown by the block above.
    {
        MsgBox("An error was thrown!`nSpecifically: " e.Message)
        Exit
    }
}