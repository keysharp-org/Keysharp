#NoTrayIcon
#Include <assert>

AssertEq(-2, Floor(-1.5), A_LineNumber)
	
AssertEq(-1, Floor(-1), A_LineNumber)

AssertEq(-1, Floor(-0.5), A_LineNumber)

AssertEq(0, Floor(0), A_LineNumber)

AssertEq(0, Floor(-0), A_LineNumber)

AssertEq(0, Floor(0.5), A_LineNumber)
	
AssertEq(1, Floor(1), A_LineNumber)

AssertEq(1, Floor(1.675), A_LineNumber)

FileAppend "pass", "*"
