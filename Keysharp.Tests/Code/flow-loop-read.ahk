#NoTrayIcon
#ErrorStdOut
#Warn All, StdOut
#Warn LocalSameAsGlobal, Off
#Include <assert>
#Include <testpaths>

x := ""

; MsgBox(A_WorkingDir)

Loop Read TestFixturePath("test-text-file.txt")
{
	x .= A_LoopReadLine
}

AssertEq(x, "this is line 1another lineline 3", A_LineNumber)

x := ""
outputPath := A_Temp . "/keysharp-loop-read-output-" . Random(1, 2147483647) . ".txt"
while FileExist(outputPath)
	outputPath := A_Temp . "/keysharp-loop-read-output-" . Random(1, 2147483647) . ".txt"

try
{
	Loop Read TestFixturePath("test-text-file.txt"), outputPath ; this is a comment
	{
		y := Random()
		x .= A_LoopReadLine
		x .= y
		z := A_LoopReadLine
		z .= y
		FileAppend(z)
	}

	z := ""

	Loop Read  outputPath ; another comment
	{
		z.= A_LoopReadLine
	}

	AssertEq(x, z, A_LineNumber)

	; Leaving the loop early closes the file.
	Loop Read outputPath
		break
}
finally
{
	if FileExist(outputPath)
		FileDelete outputPath
}

; Files without a BOM use FileEncoding; a BOM overrides that setting.
encodingPath := A_Temp . "/keysharp-loop-read-" . Random(1, 2147483647) . ".txt"
while FileExist(encodingPath)
	encodingPath := A_Temp . "/keysharp-loop-read-" . Random(1, 2147483647) . ".txt"
savedEncoding := A_FileEncoding
try
{
	for encoding in ["CP1252", "UTF-16-RAW", "UTF-8", "UTF-16"]
	{
		FileAppend("caf" . Chr(233) . "`n" . Chr(8364), encodingPath, encoding)
		FileEncoding(encoding = "UTF-8" || encoding = "UTF-16" ? "CP1252" : encoding)
		lines := []
		Loop Read encodingPath
			lines.Push(A_LoopReadLine)
		AssertEq(lines.Length, 2, A_LineNumber)
		AssertEq(lines[1], "caf" . Chr(233), A_LineNumber)
		AssertEq(lines[2], Chr(8364), A_LineNumber)
		FileDelete(encodingPath)
	}
}
finally
{
	FileEncoding(savedEncoding)
	if FileExist(encodingPath)
		FileDelete(encodingPath)
}

; A missing file raises OSError, unless an Else handles it.
ReadMissing() {
	Loop Read "missing-loop-read-input.txt"
		return
}

Throws(ReadMissing, A_LineNumber, OSError)
x := ""

Loop Read "missing-loop-read-input.txt"
	x := "body"
else
	x := "else"

AssertEq(x, "else", A_LineNumber)

; An output file of * is standard output, which the loop leaves open.
Loop Read TestFixturePath("test-text-file.txt"), "*"
	FileAppend ""

FileAppend "pass", "*"
