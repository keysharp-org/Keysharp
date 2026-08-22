#NoTrayIcon

#import KS { Cosh }
#Include <assert>
PI := 3.1415926535897931

AssertEq(11.591953275521519, Cosh(-1 * PI), A_LineNumber)

AssertEq(2.5091784786580567, Cosh(-0.5 * PI), A_LineNumber)

AssertEq(1, Cosh(0), A_LineNumber)

AssertEq(1, Cosh(-0), A_LineNumber)

AssertEq(2.5091784786580567, Cosh(0.5 * PI), A_LineNumber)
	
AssertEq(11.591953275521519, Cosh(1 * PI), A_LineNumber)

AssertEq(4.227946118844592, Cosh(0.675 * PI), A_LineNumber)

FileAppend "pass", "*"
