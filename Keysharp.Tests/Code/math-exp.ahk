#NoTrayIcon
#Include <assert>

AssertEq(0.36787944117144233, Exp(-1), A_LineNumber)

AssertEq(0.6065306597126334, Exp(-0.5), A_LineNumber)

AssertEq(1, Exp(0), A_LineNumber)

AssertEq(1, Exp(-0), A_LineNumber)

AssertEq(1.6487212707001282, Exp(0.5), A_LineNumber)
	
AssertEq(2.718281828459045, Exp(1), A_LineNumber)

AssertEq(1.9640329759698474, Exp(0.675), A_LineNumber)

FileAppend "pass", "*"
