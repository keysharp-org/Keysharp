#NoTrayIcon
#Include <assert>

AssertEq(0, Sqrt(0), A_LineNumber)

AssertEq(1, Sqrt(1), A_LineNumber)

AssertEq(2, Sqrt(4), A_LineNumber)

AssertEq(3, Sqrt(9), A_LineNumber)

AssertEq(6, Sqrt(36), A_LineNumber)
	
AssertEq(113, Sqrt(12769), A_LineNumber)

AssertEq(2.8284271247461903, Sqrt(8), A_LineNumber)

FileAppend "pass", "*"
