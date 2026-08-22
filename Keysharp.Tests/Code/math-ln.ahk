#NoTrayIcon
#Include <assert>

AssertEq(-0.6931471805599453, Ln(0.5), A_LineNumber)
	
AssertEq(0, Ln(1), A_LineNumber)

AssertEq(-0.3930425881096072, Ln(0.675), A_LineNumber)

FileAppend "pass", "*"
