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

; A midpoint rounds away from zero, not to even, which is what Convert.ToInt64 would do:
; it gives 0, 2, 0, -2, 1984 for these.
AssertEq(Round(0.5), 1, A_LineNumber)

AssertEq(Round(2.5), 3, A_LineNumber)

AssertEq(Round(-0.5), -1, A_LineNumber)

AssertEq(Round(-2.5), -3, A_LineNumber)

AssertEq(Round(1984.5), 1985, A_LineNumber)

AssertEq(Round(1984.5, 0), 1985, A_LineNumber)

; A positive N gives a String with exactly N decimal places, and a negative one an Integer.
AssertEq(Round(1.5, 2), "1.50", A_LineNumber)
AssertEq(Type(Round(1.5, 2)), "String", A_LineNumber)
AssertEq(Round(2.675, 2), "2.68", A_LineNumber)
AssertEq(Round(-0.04, 1), "-0.0", A_LineNumber)
AssertEq(Type(Round(345, -1)), "Integer", A_LineNumber)
AssertEq(Round(345, -1) "", "350", A_LineNumber)

FileAppend "pass", "*"
