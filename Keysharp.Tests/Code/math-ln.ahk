#NoTrayIcon
#Include <assert>

AssertEq(-0.6931471805599453, Ln(0.5), A_LineNumber)
	
AssertEq(0, Ln(1), A_LineNumber)

AssertEq(-0.3930425881096072, Ln(0.675), A_LineNumber)

; Zero is in the domain and yields negative infinity.
zeroLn := Ln(0)

Assert(zeroLn is Float && zeroLn < -1.0E300, A_LineNumber)

Throws(() => Ln(-1), A_LineNumber, Error)

Throws(() => Ln(-0.5), A_LineNumber, Error)

FileAppend "pass", "*"
