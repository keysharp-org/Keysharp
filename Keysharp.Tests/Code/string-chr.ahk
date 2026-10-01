#NoTrayIcon
#Include <assert>

x := Chr(116)

AssertEq(x, "t", A_LineNumber)

; A surrogate code unit comes back as it is, and a value outside Unicode is a ValueError.
AssertEq(StrLen(Chr(0xD83D)), 1, A_LineNumber)
AssertEq(Ord(Chr(0xD83D)), 0xD83D, A_LineNumber)
AssertEq(StrLen(Chr(0x1F600)), 2, A_LineNumber)
Throws(() => Chr(-1), A_LineNumber, ValueError)
Throws(() => Chr(0x110000), A_LineNumber, ValueError)
Throws(() => Chr("x"), A_LineNumber, TypeError)

FileAppend "pass", "*"
