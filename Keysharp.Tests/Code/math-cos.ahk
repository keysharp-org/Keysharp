#NoTrayIcon
#Include <assert>

PI := 3.1415926535897931

AssertEq(-1, Cos(-1 * PI), A_LineNumber)

AssertEq(6.123233995736766E-17, Cos(-0.5 * PI), A_LineNumber)

AssertEq(1, Cos(0), A_LineNumber)

AssertEq(1, Cos(-0), A_LineNumber)

AssertEq(6.123233995736766E-17, Cos(0.5 * PI), A_LineNumber)
	
AssertEq(-1, Cos(1 * PI), A_LineNumber)

AssertEq(-0.5224985647159488, Cos(0.675 * PI), A_LineNumber)

FileAppend "pass", "*"
