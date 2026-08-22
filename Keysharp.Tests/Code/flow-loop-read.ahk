#NoTrayIcon
#Include <assert>

x := ""

; MsgBox(A_WorkingDir)

Loop Read "../../../Keysharp.Tests/Code/test-text-file.txt"
{
	x .= A_LoopReadLine
}

AssertEq(x, "this is line 1another lineline 3", A_LineNumber)

x := ""
FileDelete "../../../Keysharp.Tests/Code/test-text-file-out.txt"

Loop Read "../../../Keysharp.Tests/Code/test-text-file.txt", "../../../Keysharp.Tests/Code/test-text-file-out.txt" ; this is a comment
{
	y := Random()
	x .= A_LoopReadLine
	x .= y
	z := A_LoopReadLine
	z .= y
	FileAppend(z)
}

z := ""

Loop Read  "../../../Keysharp.Tests/Code/test-text-file-out.txt" ; another comment
{
	z.= A_LoopReadLine
}

AssertEq(x, z, A_LineNumber)

FileAppend "pass", "*"
