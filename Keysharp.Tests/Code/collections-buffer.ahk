#NoTrayIcon
#ErrorStdOut
#Warn All, StdOut
#Include <assert>

buf := Buffer(5, 10)

AssertEq(buf.Size, 5, A_LineNumber)
	
Loop (buf.Size)
{
	p := buf[A_Index]
	
	AssertEq(p, 10, A_LineNumber)
}

buf.Size := 10

AssertEq(buf.Size, 10, A_LineNumber)
	
; Ensure original values were copied. Subsequent values are undefined.
Loop (5)
{
	p := buf[A_Index]
	
	AssertEq(p, 10, A_LineNumber)
}

; A smaller size keeps the bytes that fit, 0 frees the memory, and a negative size is an error, as in AutoHotkey.
buf.Size := 3
AssertEq(buf.Size, 3, A_LineNumber)
AssertEq(buf[3], 10, A_LineNumber)
buf.Size := 0
AssertEq(buf.Ptr, 0, A_LineNumber)
Throws(() => buf.Size := -1, A_LineNumber, ValueError)
AssertEq(buf.Size, 0, A_LineNumber)

buf := Buffer(4, 23)
copy := buf.Clone()
Assert(copy.Ptr != buf.Ptr, A_LineNumber)
copy.Size := 2
AssertEq(buf.Size, 4, A_LineNumber)
buf.Size := 0
AssertEq(copy.Size, 2, A_LineNumber)
AssertEq(copy[1], 23, A_LineNumber)
AssertEq(copy[2], 23, A_LineNumber)
copy.Size := 0

FileAppend "pass", "*"
