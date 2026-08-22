#NoTrayIcon
#Include <assert>

x := 1

Assert(x > 0 and x < 2, A_LineNumber)

Assert(!(x > 2 and x < 0), A_LineNumber)
	
Assert(!(x > 2 and x < 3), A_LineNumber)

Assert(x > 0.9 and x < 1.1, A_LineNumber)

Assert(!(x > 1.1 and x < 0.9), A_LineNumber)
	
Assert(!(x > 0.5 and x < 0.8), A_LineNumber)

Assert(x > -1 and x < 2, A_LineNumber)

Assert(!(x > 2 and x < -1), A_LineNumber)

Assert(!(x > -3 and x < -2), A_LineNumber)
	
Assert(!(x > -2 and x < -3), A_LineNumber)
	
Assert(x > -0.9 and x < 1.1, A_LineNumber)

Assert(!(x > 1.1 and x < -0.9), A_LineNumber)

Assert(!(x > -0.5 and x < -0.8), A_LineNumber)

FileAppend "pass", "*"
