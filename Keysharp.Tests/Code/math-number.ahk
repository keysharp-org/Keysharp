#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#Include <assert>

AssertEq(Number("0"), 0, A_LineNumber)

AssertEq(Type(Number("0")), "Integer", A_LineNumber)

AssertEq(Number("0.0"), 0, A_LineNumber)

AssertEq(Type(Number("0.0")), "Float", A_LineNumber)

AssertEq(Number("1"), 1, A_LineNumber)

AssertEq(Type(Number("1.0")), "Float", A_LineNumber)

AssertEq(Number("1.5"), 1.5, A_LineNumber)

AssertEq(Number("-1"), -1, A_LineNumber)

AssertEq(Number("-1.0"), -1.0, A_LineNumber)

AssertEq(Type(Number("-1.0")), "Float", A_LineNumber)

AssertEq(Number("0xF"), 15, A_LineNumber)

AssertEq(Number("-0xF"), -15, A_LineNumber)

; A number passes through with its type unchanged.
AssertEq(Number(1), 1, A_LineNumber)

AssertEq(Number(-1), -1, A_LineNumber)

AssertEq(Number(1.5), 1.5, A_LineNumber)

AssertEq(Type(Number(1.0)), "Float", A_LineNumber)

Throws(() => Number("asdf"), A_LineNumber, TypeError)

; Trailing characters make a string non-numeric, including C#-style type suffixes.
Throws(() => Number("1L"), A_LineNumber, TypeError)

Throws(() => Number("1.0D"), A_LineNumber, TypeError)

Throws(() => Number("1x"), A_LineNumber, TypeError)

AssertEq(IsNumber("1L"), 0, A_LineNumber)

AssertEq(" 12 " + 1, 13, A_LineNumber)

FileAppend "pass", "*"
