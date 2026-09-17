#NoTrayIcon
#Include <assert>

x := Chr(116)

AssertEq(x, "t", A_LineNumber)

FileAppend "pass", "*"
