#NoTrayIcon
#ErrorStdOut
#Warn All, StdOut
#Include <assert>
#import KS { Font }

; The script-visible RichEdit surface. Everything checked here is synchronous and needs no window on screen,
; so the control is added but never shown: what matters is that the members read and write the same widget
; state a script would see, and that character positions line up with the text Value returns.

g := Gui()
re := g.Add("RichEdit", "w400 h200 -Wrap")

AssertEq(Type(re), "RichEdit", A_LineNumber)
Assert(re.Type = "richedit", A_LineNumber)   ; Type echoes the caller's spelling, so match case-insensitively
AssertEq(Type(g.AddRichEdit("w10 h10")), "RichEdit", A_LineNumber)

; --- positions line up with Value -------------------------------------------------------------------------

re.Value := "one`ntwo`nthree"
AssertEq(re.Value, "one`ntwo`nthree", A_LineNumber)
AssertEq(re.TextLength, StrLen(re.Value), A_LineNumber)
AssertEq(re.LineCount, 3, A_LineNumber)
AssertEq(re.GetLine(1), "one", A_LineNumber)
AssertEq(re.GetLine(2), "two", A_LineNumber)
AssertEq(re.GetLine(3), "three", A_LineNumber)
AssertEq(re.LineLength(2), 3, A_LineNumber)

; PosFromLine is 1-based and indexes the same string Value returns.
AssertEq(re.PosFromLine(1), 1, A_LineNumber)
AssertEq(re.PosFromLine(2), 5, A_LineNumber)
AssertEq(re.PosFromLine(3), 9, A_LineNumber)
AssertEq(SubStr(re.Value, re.PosFromLine(2), 3), "two", A_LineNumber)
AssertEq(re.LineFromPos(1), 1, A_LineNumber)
AssertEq(re.LineFromPos(5), 2, A_LineNumber)
AssertEq(re.LineFromPos(9), 3, A_LineNumber)

; Out-of-range line and position arguments clamp rather than throw.
AssertEq(re.GetLine(99), "three", A_LineNumber)
AssertEq(re.LineFromPos(0), 1, A_LineNumber)
AssertEq(re.LineFromPos(9999), 3, A_LineNumber)

; --- selection --------------------------------------------------------------------------------------------

re.Select(5, 3)
AssertEq(re.SelectionStart, 5, A_LineNumber)
AssertEq(re.SelectionLength, 3, A_LineNumber)
AssertEq(re.SelectedText, "two", A_LineNumber)
AssertEq(re.CurrentLine, 2, A_LineNumber)
AssertEq(re.CurrentCol, 1, A_LineNumber)

re.SelectionStart := 7
AssertEq(re.SelectionLength, 0, A_LineNumber)
AssertEq(re.CurrentCol, 3, A_LineNumber)

re.SelectAll()
AssertEq(re.SelectionStart, 1, A_LineNumber)
AssertEq(re.SelectionLength, StrLen(re.Value), A_LineNumber)

re.Select(5, 3)
re.SelectedText := "TWO"
AssertEq(re.Value, "one`nTWO`nthree", A_LineNumber)

; A length past the end is clamped to what is there.
re.Select(1, 9999)
AssertEq(re.SelectionLength, StrLen(re.Value), A_LineNumber)

; --- editing ----------------------------------------------------------------------------------------------

re.Value := "one`ntwo"
re.Append("`nthree")
AssertEq(re.Value, "one`ntwo`nthree", A_LineNumber)

re.Replace(5, 3, "2")
AssertEq(re.Value, "one`n2`nthree", A_LineNumber)

re.ScrollCaret()

; --- Find -----------------------------------------------------------------------------------------------

re.Value := "alpha beta Alpha gamma"
AssertEq(re.Find("alpha"), 1, A_LineNumber)
AssertEq(re.Find("alpha", 2), 12, A_LineNumber)            ; case-insensitive by default
AssertEq(re.Find("alpha", 2, "MatchCase"), 0, A_LineNumber)
AssertEq(re.Find("Alpha", 1, "MatchCase"), 12, A_LineNumber)
AssertEq(re.Find("alpha", , "Reverse"), 12, A_LineNumber)
AssertEq(re.Find("alp", 1, "WholeWord"), 0, A_LineNumber)
AssertEq(re.Find("beta", 1, "WholeWord"), 7, A_LineNumber)
AssertEq(re.Find("nothing"), 0, A_LineNumber)
AssertEq(re.Find(""), 0, A_LineNumber)
; Find only reports; it does not move the selection.
re.Select(1, 0)
AssertEq(re.Find("gamma"), 18, A_LineNumber)
AssertEq(re.SelectionStart, 1, A_LineNumber)
Throws(() => re.Find("alpha", 1, "Sideways"), A_LineNumber)

; --- character formatting ---------------------------------------------------------------------------------

re.Value := "one`ntwo`nthree"
re.SetFormat(1, 3, "cRed bold")
f := re.GetFormat(1, 3)
AssertEq(Type(f), "Font", A_LineNumber)
AssertEq(f.Color, "FF0000", A_LineNumber)
AssertEq(f.Bold, true, A_LineNumber)

; Formatting one range leaves the next alone, and an option that is not mentioned is left as it was.
f2 := re.GetFormat(5, 3)
AssertEq(f2.Bold, false, A_LineNumber)
re.SetFormat(5, 3, "italic")
f2 := re.GetFormat(5, 3)
AssertEq(f2.Italic, true, A_LineNumber)
AssertEq(f2.Bold, false, A_LineNumber)

; A Ks.Font applies the same way an option string does.
spec := Font("cBlue underline")
re.SetFormat(9, 5, spec)
f3 := re.GetFormat(9, 5)
AssertEq(f3.Color, "0000FF", A_LineNumber)
AssertEq(f3.Underline, true, A_LineNumber)

; Background is its own option, read back with GetBackColor.
re.SetFormat(1, 3, "BackgroundYellow")
AssertEq(re.GetBackColor(1, 3), "FFFF00", A_LineNumber)
re.SetFormat(1, 3, "BackgroundDefault")
; Off Windows a text tag carries no alpha, so "no background" has to be painted as the control's own colour;
; only the Win32 control can report the range as having none.
if (A_OSType = "WINDOWS")
	AssertEq(re.GetBackColor(1, 3), "", A_LineNumber)

; The formatting call does not disturb the selection.
re.Select(5, 3)
re.SetFormat(1, 3, "cGreen")
AssertEq(re.SelectionStart, 5, A_LineNumber)
AssertEq(re.SelectionLength, 3, A_LineNumber)

; A start of 0 means "the current selection".
re.Select(5, 3)
re.SetFormat(0, 0, "cRed")
AssertEq(re.GetFormat(5, 3).Color, "FF0000", A_LineNumber)
AssertEq(re.GetFormat().Color, "FF0000", A_LineNumber)

Throws(() => re.SetFormat(1, 3, "cNotAColour"), A_LineNumber)
Throws(() => re.SetFormat(1, 3, "wobbly"), A_LineNumber)

; --- batching ---------------------------------------------------------------------------------------------

re.Value := "one`ntwo`nthree"
re.Select(9, 5)
re.BeginUpdate()
re.BeginUpdate()
re.SetFormat(1, 3, "cRed")
re.SetFormat(5, 3, "cBlue")
re.EndUpdate()
re.EndUpdate()
AssertEq(re.GetFormat(1, 3).Color, "FF0000", A_LineNumber)
AssertEq(re.GetFormat(5, 3).Color, "0000FF", A_LineNumber)
; The pair puts the selection back that it found.
AssertEq(re.SelectionStart, 9, A_LineNumber)
AssertEq(re.SelectionLength, 5, A_LineNumber)
Throws(() => re.EndUpdate(), A_LineNumber)

; --- state ------------------------------------------------------------------------------------------------

re.Modified := false
AssertEq(re.Modified, false, A_LineNumber)
re.Append("!")
AssertEq(re.Modified, true, A_LineNumber)

re.ReadOnly := true
AssertEq(re.ReadOnly, true, A_LineNumber)
re.ReadOnly := false

re.WordWrap := true
AssertEq(re.WordWrap, true, A_LineNumber)
re.WordWrap := false
AssertEq(re.WordWrap, false, A_LineNumber)

AssertEq(re.Zoom, 1, A_LineNumber)
re.Zoom := 2
AssertEq(re.Zoom, 2, A_LineNumber)
re.Zoom := 1
Throws(() => re.Zoom := 0, A_LineNumber)
Throws(() => re.Zoom := 1000, A_LineNumber)

; --- files ------------------------------------------------------------------------------------------------

txtPath := A_Temp . "/ks-richedit-test.txt"
re.Value := "saved`ntext"
re.SaveFile(txtPath)
AssertEq(StrReplace(FileRead(txtPath), "`r`n", "`n"), "saved`ntext", A_LineNumber)
re.Value := ""
re.LoadFile(txtPath)
AssertEq(re.Value, "saved`ntext", A_LineNumber)
FileDelete(txtPath)
Throws(() => re.SaveFile(txtPath, "docx"), A_LineNumber)
Throws(() => re.LoadFile(A_Temp . "/ks-richedit-missing.txt", "Text"), A_LineNumber)

; --- events -----------------------------------------------------------------------------------------------

; Counted on an object: a handler cannot assign to an outer variable, but it can set a property.
seen := {changes: 0, selchanges: 0}
re.OnEvent("Change", (*) => seen.changes += 1)
re.OnEvent("SelectionChange", (*) => seen.selchanges += 1)
; A GUI event runs from the queue, not inside the statement which raised it, so each of these yields before
; asking what happened - including the ones expecting nothing, which would otherwise pass without waiting.
re.Value := "abc"
Sleep(50)
Assert(seen.changes > 0, A_LineNumber)

; Formatting is not an edit, so it raises no Change.
before := seen.changes
re.SetFormat(1, 3, "cRed")
Sleep(50)
AssertEq(seen.changes, before, A_LineNumber)

re.Select(2, 1)
Sleep(50)
Assert(seen.selchanges > 0, A_LineNumber)

; Neither applying nor reading formatting is a selection change, though both have to move the selection.
before := seen.selchanges
re.SetFormat(1, 2, "cBlue")
re.GetFormat(1, 2)
re.GetBackColor(1, 2)
Sleep(50)
AssertEq(seen.selchanges, before, A_LineNumber)
AssertEq(re.SelectionStart, 2, A_LineNumber)
AssertEq(re.SelectionLength, 1, A_LineNumber)

; A RichEdit's own events belong to no other control, and it takes none it cannot raise.
btn := g.Add("Button", "w80", "ok")
Throws(() => btn.OnEvent("SelectionChange", Noop), A_LineNumber)
Throws(() => btn.OnEvent("LinkClick", Noop), A_LineNumber)
Throws(() => re.OnEvent("ItemCheck", Noop), A_LineNumber)
re.OnEvent("LinkClick", Noop)
re.OnEvent("LinkClick", Noop, 0)

; --- the members other control types do not have ----------------------------------------------------------

edit := g.Add("Edit", "w80")
Throws(() => Type(edit.RichText), A_LineNumber)   ; wrapped: a discarded property read in a lambda is not evaluated
Throws(() => edit.SetFormat(1, 1, "cRed"), A_LineNumber)

; --- what only some platforms can do ----------------------------------------------------------------------

if (A_OSType = "WINDOWS") {
	re.Value := "one`ntwo`nthree"
	re.RichText := "{\rtf1\ansi bold\par}"
	Assert(InStr(re.RichText, "\rtf1") > 0, A_LineNumber)
	Assert(InStr(re.Value, "bold") > 0, A_LineNumber)
	Throws(() => re.RichText := "not rtf at all", A_LineNumber)

	re.Value := "one`ntwo`nthree"
	re.Select(1, 3)
	Assert(InStr(re.SelectedRichText, "\rtf1") > 0, A_LineNumber)

	re.SetParagraph(1, 3, "Center Indent20")
	para := re.GetParagraph(1)
	Assert(InStr(para, "Center") > 0, A_LineNumber)
	Assert(InStr(para, "Indent20") > 0, A_LineNumber)
	re.SetParagraph(1, 3, "Left Indent0")
	Throws(() => re.SetParagraph(1, 3, "Sideways"), A_LineNumber)

	AssertEq(re.FirstVisibleLine, 1, A_LineNumber)

	; Hit testing round-trips: the point a character is drawn at maps back to that character.
	pt := re.PointFromPos(5)
	AssertEq(re.PosFromPoint(pt.X, pt.Y), 5, A_LineNumber)

	re.DetectUrls := true
	AssertEq(re.DetectUrls, true, A_LineNumber)
	re.HideSelection := false
	AssertEq(re.HideSelection, false, A_LineNumber)

	re.Value := "undo me"
	re.ClearUndo()
	AssertEq(re.CanUndo, false, A_LineNumber)
	re.Select(1, 4)
	re.SelectedText := "redo"
	AssertEq(re.Value, "redo me", A_LineNumber)
	Assert(re.CanUndo, A_LineNumber)
	re.Undo()
	AssertEq(re.Value, "undo me", A_LineNumber)
	Assert(re.CanRedo, A_LineNumber)
	re.Redo()
	AssertEq(re.Value, "redo me", A_LineNumber)
} else {
	; Named gaps report themselves rather than doing nothing or making an answer up. The property reads are
	; wrapped for the same reason as edit.RichText above.
	Throws(() => Type(re.SelectedRichText), A_LineNumber)
	Throws(() => re.SetParagraph(1, 3, "Center"), A_LineNumber)
	Throws(() => re.PointFromPos(1), A_LineNumber)
	Throws(() => Type(re.FirstVisibleLine), A_LineNumber)

	; GTK's text widget knows nothing of RTF at all; Cocoa's does.
	if (A_OSType = "LINUX")
		Throws(() => Type(re.RichText), A_LineNumber)
	else
		Assert(InStr(re.RichText, "\rtf1") > 0, A_LineNumber)

	AssertEq(re.CanUndo, false, A_LineNumber)
	re.Undo()
}

Noop(*) {
}

FileAppend("pass`n", "*")
