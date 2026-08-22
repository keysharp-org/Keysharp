#NoTrayIcon
#Include <assert>

PI := 3.1415926535897931

AssertEq(-1.2246467991473532E-16, Sin(-1 * PI), A_LineNumber)

AssertEq(-1, Sin(-0.5 * PI), A_LineNumber)

AssertEq(0, Sin(0), A_LineNumber)

AssertEq(0, Sin(-0), A_LineNumber)

AssertEq(1, Sin(0.5 * PI), A_LineNumber)
	
AssertEq(1.2246467991473532E-16, Sin(1 * PI), A_LineNumber)

AssertEq(0.8526401643540923, Sin(0.675 * PI), A_LineNumber)

FileAppend "pass", "*"
