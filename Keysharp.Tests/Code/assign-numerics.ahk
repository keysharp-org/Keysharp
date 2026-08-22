#NoTrayIcon
#Include <assert>

x := 1

AssertEq(x, 1, A_LineNumber)

Assert(!(x != 1), A_LineNumber)
	
xneg := -1

AssertEq(xneg, -1, A_LineNumber)

Assert(!(xneg != -1), A_LineNumber)

FileAppend "pass", "*"
