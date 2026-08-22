#NoTrayIcon
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

FileAppend "pass", "*"
