#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#Import KS { EventHook }
#Include <assert>

; Options may be separated by spaces or run together, and a number ends at the next letter.
for options, bufferLength in Map("B C H I10 M L1 T2 V * E", 1, "BCHI10ML123T2V*E", 123)
{
	ih := InputHook(options)
	AssertEq(ih.BackspaceIsUndo, false, A_LineNumber)
	AssertEq(ih.CaseSensitive, true, A_LineNumber)
	AssertEq(ih.BeforeHotkeys, true, A_LineNumber)
	AssertEq(ih.MinSendLevel, 10, A_LineNumber)
	AssertEq(ih.TranscribeModifiedKeys, true, A_LineNumber)
	AssertEq(ih.BufferLengthMax, bufferLength, A_LineNumber)
	AssertEq(ih.Timeout, 2, A_LineNumber)
	AssertEq(ih.VisibleText, true, A_LineNumber)
	AssertEq(ih.VisibleNonText, true, A_LineNumber)
	AssertEq(ih.FindAnywhere, true, A_LineNumber)
	AssertEq(ih.EndCharMode, true, A_LineNumber)
}

; A number option ending the text takes its default, as AutoHotkey reads the string's terminator there.
AssertEq(InputHook("I").MinSendLevel, 1, A_LineNumber)
AssertEq(InputHook("L").BufferLengthMax, 0, A_LineNumber)
AssertEq(InputHook("T").Timeout, 0, A_LineNumber)

; Timeout takes a number, as AutoHotkey's does.
ih := InputHook()
ih.Timeout := 1.5
AssertEq(ih.Timeout, 1.5, A_LineNumber)
Throws(() => ih.Timeout := "abc", A_LineNumber, TypeError)

; A Boolean property reads its value's truth as `if` does.
ih.CaseSensitive := 2
AssertEq(ih.CaseSensitive, true, A_LineNumber)
ih.FindAnywhere := "yes"
AssertEq(ih.FindAnywhere, true, A_LineNumber)
ih.FindAnywhere := "0"
AssertEq(ih.FindAnywhere, false, A_LineNumber)

; A key code beyond the key tables names no key, so it is ignored like an unknown key name.
try
{
	InputHook("", "{vk100}{sc3FF}")
	ih.KeyOpt("{vk1FF}{sc3FF}", "E")
}
catch
	Assert(false, A_LineNumber)

; A hook which never ran, or was stopped, is idle and reads Stopped, as in AutoHotkey, and Wait returns that at once.
ih := InputHook()
Assert(ih is EventHook, A_LineNumber)
AssertEq(ih.InProgress, false, A_LineNumber)
AssertEq(ih.EndReason, "Stopped", A_LineNumber)
ih.Stop()
AssertEq(ih.EndReason, "Stopped", A_LineNumber)
AssertEq(ih.Wait(), "Stopped", A_LineNumber)

; A callback property takes something the input can call with its arguments, as AutoHotkey checks, and "" clears
; it. A refused value leaves the property as it was.
CharCallback(hook, char) => 0
ih.OnChar := CharCallback
Assert(ih.OnChar is Func, A_LineNumber)
Throws(() => ih.OnEnd := CharCallback, A_LineNumber, ValueError)
Throws(() => ih.OnKeyDown := "not a function", A_LineNumber, TypeError)
Throws(() => ih.OnChar := "not a function", A_LineNumber, TypeError)
Throws(() => ih.OnChar := {}, A_LineNumber, MethodError)
Assert(ih.OnChar is Func, A_LineNumber)
ih.OnChar := ""
AssertEq(ih.OnChar, "", A_LineNumber)

FileAppend "pass", "*"
