#NoTrayIcon
#Include <assert>

AssertEq(-1, Ceil(-1), A_LineNumber)

AssertEq(-2, Ceil(-2.1), A_LineNumber)

AssertEq(0, Ceil(0), A_LineNumber)

AssertEq(0, Ceil(-0), A_LineNumber)
	
AssertEq(2, Ceil(1.000001), A_LineNumber)

FileAppend "pass", "*"
