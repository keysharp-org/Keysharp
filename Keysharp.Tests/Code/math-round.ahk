#NoTrayIcon
#Include <assert>

val := Round(3.14)

AssertEq(val, 3, A_LineNumber)
	
AssertEq(Type(val), "Integer", A_LineNumber)
		
AssertEq(Round(3.14, 1), 3.1, A_LineNumber)
		
AssertEq(Round(345, -1), 350, A_LineNumber)
		
AssertEq(Round(345, -2), 300, A_LineNumber)

AssertEq(Round(-345, -1), -350, A_LineNumber)
		
AssertEq(Round(-345, -2), -300, A_LineNumber)

AssertEq(Round(-0, -2), 0, A_LineNumber)

FileAppend "pass", "*"
