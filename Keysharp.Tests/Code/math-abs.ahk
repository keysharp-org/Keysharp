#NoTrayIcon
#Include <assert>

AssertEq(1, Abs(1), A_LineNumber)

AssertEq(1, Abs(-1), A_LineNumber)

AssertEq(9.81, Abs(-9.81), A_LineNumber)

AssertEq(0, Abs(0), A_LineNumber)

AssertEq(0, Abs(-0), A_LineNumber)

FileAppend "pass", "*"
